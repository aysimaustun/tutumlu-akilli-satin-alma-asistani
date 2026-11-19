using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Matching;

// Finds and repairs Offers that were merged onto the wrong Products row by an earlier,
// looser version of TitleSimilarity (see its containment-shortcut fix). Re-scores every
// currently-live offer against its product's canonical title with the corrected logic —
// anything now below MatchingService.MatchThreshold is a suspected bad merge.
public static class ProductMatchReviewer
{
    public record SuspectMatch(
        int ProductId, string CanonicalTitle, int OfferId, int SellerId, string SellerName,
        int SellerProductId, string ExternalTitle, double Score);

    public record FixOutcome(
        int OfferId, string SellerName, string ExternalTitle, int OldProductId, int? NewProductId, string Action);

    public static async Task<List<SuspectMatch>> FindSuspectsAsync(SqlConnection db)
    {
        var rows = await db.QueryAsync<(int ProductId, string CanonicalTitle, string? BrandName, int OfferId,
            int SellerId, string SellerName, int SellerProductId, string ExternalTitle)>(@"
            SELECT p.Id AS ProductId, p.Name AS CanonicalTitle, b.Name AS BrandName,
                   o.Id AS OfferId, o.SellerId, s.Name AS SellerName,
                   sp.Id AS SellerProductId, sp.ExternalTitle
            FROM Offers o
            JOIN Products p ON p.Id = o.ProductId
            JOIN SellerProducts sp ON sp.Id = o.SellerProductId
            JOIN Sellers s ON s.Id = o.SellerId
            LEFT JOIN Brands b ON b.Id = p.BrandId");

        var suspects = new List<SuspectMatch>();
        foreach (var row in rows)
        {
            var score = TitleSimilarity.Calculate(row.CanonicalTitle, row.ExternalTitle, row.BrandName);
            if (score < MatchingService.MatchThreshold)
            {
                suspects.Add(new SuspectMatch(
                    row.ProductId, row.CanonicalTitle, row.OfferId, row.SellerId, row.SellerName,
                    row.SellerProductId, row.ExternalTitle, score));
            }
        }

        return suspects;
    }

    // Moves each suspect offer off its current (wrongly-matched) product: onto an existing
    // product that now scores a real match under the same brand/size gate, or a freshly
    // created one otherwise — mirroring exactly what MatchProductAsync would have done at
    // scrape time had the title logic been correct then. Processes suspects one at a time
    // and re-queries Products fresh each iteration, so later suspects can land on a product
    // created earlier in the same run instead of each spawning its own duplicate.
    public static async Task<List<FixOutcome>> FixSuspectsAsync(SqlConnection db)
    {
        var suspects = await FindSuspectsAsync(db);
        var outcomes = new List<FixOutcome>();

        foreach (var suspect in suspects)
        {
            var current = await db.QuerySingleAsync<(int BrandId, decimal? SizeValue, string? SizeUnit, int PackQuantity, int? CategoryId)>(
                "SELECT BrandId, SizeValue, SizeUnit, PackQuantity, CategoryId FROM Products WHERE Id = @Id",
                new { Id = suspect.ProductId });

            var brandName = await db.QuerySingleOrDefaultAsync<string>(
                "SELECT Name FROM Brands WHERE Id = @Id", new { Id = current.BrandId }) ?? "Unknown";

            var (matchId, score) = await MatchingService.FindBestMatchAsync(
                db, current.BrandId, current.SizeValue, current.SizeUnit, current.PackQuantity,
                suspect.ExternalTitle, brandName, excludeProductId: suspect.ProductId);

            // Offers has UNIQUE(ProductId, SellerId) — if this seller already has a (different)
            // offer sitting on the target product, moving this one there would collide. Skip
            // it for manual review instead of silently dropping/overwriting either offer.
            var targetProductId = matchId.HasValue && score >= MatchingService.MatchThreshold ? matchId.Value : (int?)null;
            string action;

            if (targetProductId is null)
            {
                targetProductId = await db.QuerySingleAsync<int>(@"
                    INSERT INTO Products (Name, BrandId, SizeValue, SizeUnit, PackQuantity, CategoryId, ImageUrl)
                    OUTPUT INSERTED.Id
                    SELECT @Title, @BrandId, @SizeValue, @SizeUnit, @PackQuantity, @CategoryId, sp.ExternalImageUrl
                    FROM SellerProducts sp WHERE sp.Id = @SellerProductId",
                    new
                    {
                        Title = suspect.ExternalTitle, BrandId = current.BrandId, current.SizeValue, current.SizeUnit,
                        current.PackQuantity, current.CategoryId, suspect.SellerProductId
                    });
                action = "Created new product";
            }
            else
            {
                action = "Moved to existing product";
            }

            var conflict = await db.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Offers WHERE ProductId = @ProductId AND SellerId = @SellerId AND Id <> @OfferId",
                new { ProductId = targetProductId, suspect.SellerId, suspect.OfferId });

            if (conflict.HasValue)
            {
                outcomes.Add(new FixOutcome(
                    suspect.OfferId, suspect.SellerName, suspect.ExternalTitle, suspect.ProductId, null,
                    "Skipped: seller already has an offer on the target product (needs manual review)"));
                continue;
            }

            await db.ExecuteAsync(
                "UPDATE Offers SET ProductId = @ProductId WHERE Id = @OfferId",
                new { ProductId = targetProductId, suspect.OfferId });

            await db.ExecuteAsync(
                @"INSERT INTO ProductMatchingLogs (SellerProductId, ProductId, Status, MatchedBy, Notes)
                  VALUES (@SellerProductId, @ProductId, 'Rematched', 'ProductMatchReviewer', @Notes)",
                new
                {
                    suspect.SellerProductId, ProductId = targetProductId,
                    Notes = $"movedFrom={suspect.ProductId}; score={score:F2}"
                });

            outcomes.Add(new FixOutcome(
                suspect.OfferId, suspect.SellerName, suspect.ExternalTitle, suspect.ProductId, targetProductId, action));
        }

        return outcomes;
    }
}
