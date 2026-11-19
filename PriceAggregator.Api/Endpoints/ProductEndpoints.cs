using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;
using PriceAggregator.Core;
using StackExchange.Redis;

namespace PriceAggregator.Api.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/products/{productId}", async (int productId) =>
        {
            using var db = new SqlConnection(connectionString);
            var offers = await db.QueryAsync<OfferRow>(@"
                SELECT p.Name AS CanonicalTitle, s.Name AS Source, o.Price,
                       sp.ExternalImageUrl AS ImageUrl, sp.ExternalUrl AS ProductUrl,
                       p.SizeValue, p.SizeUnit, p.PackQuantity
                FROM Offers o
                JOIN Products p ON p.Id = o.ProductId
                JOIN Sellers s ON s.Id = o.SellerId
                JOIN SellerProducts sp ON sp.Id = o.SellerProductId
                WHERE o.ProductId = @ProductId
                ORDER BY o.Price ASC",
                new { ProductId = productId });

            var result = offers.Select(o =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(o.Price, o.SizeValue, o.SizeUnit, o.PackQuantity);
                return new
                {
                    o.CanonicalTitle,
                    o.Source,
                    o.Price,
                    o.ImageUrl,
                    o.ProductUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label
                };
            });

            return Results.Ok(result);
        });

        app.MapGet("/api/products/{productId}/similar", async (int productId, ICacheService cache) =>
        {
            var cacheKey = $"similar_products_{productId}";
            var result = await cache.GetOrSetAsync<List<SimilarProductRow>>(cacheKey, TimeSpan.FromHours(1), async () =>
            {
                using var db = new SqlConnection(connectionString);

                var target = await db.QuerySingleOrDefaultAsync<TargetProductRow>(@"
                    SELECT p.Name, p.CategoryId, p.BrandId, root.Name AS SectorName
                    FROM Products p
                    LEFT JOIN Categories c ON c.Id = p.CategoryId
                    LEFT JOIN Categories root ON root.Id = c.SectorId
                    WHERE p.Id = @Id",
                    new { Id = productId });

                if (target is null) return new List<SimilarProductRow>();

                var isMobilya = target.SectorName == SectorClassifier.MobilyaSectorName;

                var scored = Enumerable.Empty<ScoredProduct>();
                if (target.CategoryId is not null)
                {
                    var scopeCategoryId = target.CategoryId.Value;
                    if (!isMobilya)
                    {
                        scopeCategoryId = await db.QuerySingleOrDefaultAsync<int?>(@"
                            ;WITH Ancestors AS (
                                SELECT Id, ParentCategoryId, SectorId, 0 AS Depth FROM Categories WHERE Id = @CategoryId
                                UNION ALL
                                SELECT c.Id, c.ParentCategoryId, c.SectorId, a.Depth + 1
                                FROM Categories c JOIN Ancestors a ON c.Id = a.ParentCategoryId
                            )
                            SELECT TOP 1 Id FROM Ancestors WHERE ParentCategoryId = SectorId ORDER BY Depth ASC",
                            new { target.CategoryId }) ?? target.CategoryId.Value;
                    }

                    var candidates = await db.QueryAsync<ProductListRow>(@"
                        WITH CategoryTree AS (
                            SELECT Id FROM Categories WHERE Id = @ScopeCategoryId
                            UNION ALL
                            SELECT c.Id FROM Categories c JOIN CategoryTree ct ON c.ParentCategoryId = ct.Id
                        )
                        SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                               MIN(o.Price) AS LowestPrice,
                               COUNT(o.Id) AS OfferCount,
                               p.ImageUrl,
                               p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                               (SELECT TOP 1 o2.RegularPrice FROM Offers o2 WHERE o2.ProductId = p.Id ORDER BY o2.Price ASC) AS OldPrice
                        FROM Products p
                        LEFT JOIN Brands b ON b.Id = p.BrandId
                        JOIN Offers o ON o.ProductId = p.Id
                        WHERE p.Id <> @ProductId
                        AND p.CategoryId IN (SELECT Id FROM CategoryTree)
                        GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId",
                        new { ProductId = productId, ScopeCategoryId = scopeCategoryId });

                    scored = candidates.Select(c => new ScoredProduct(
                        c, TitleSimilarity.Calculate(c.CanonicalTitle, target.Name, c.Brand), c.CategoryId == target.CategoryId));
                }

                var threshold = isMobilya ? 0.40 : 0.60;
                var eligible = (isMobilya ? scored.Where(x => x.Score >= threshold) : scored).ToList();

                if (eligible.Count == 0 && isMobilya)
                {
                    eligible = scored.ToList();
                }

                if (eligible.Count == 0 && target.BrandId is not null)
                {
                    var brandCandidates = await db.QueryAsync<ProductListRow>(@"
                        SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                               MIN(o.Price) AS LowestPrice,
                               COUNT(o.Id) AS OfferCount,
                               p.ImageUrl,
                               p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                               (SELECT TOP 1 o2.RegularPrice FROM Offers o2 WHERE o2.ProductId = p.Id ORDER BY o2.Price ASC) AS OldPrice
                        FROM Products p
                        LEFT JOIN Brands b ON b.Id = p.BrandId
                        JOIN Offers o ON o.ProductId = p.Id
                        WHERE p.Id <> @ProductId AND p.BrandId = @BrandId
                        GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId
                        ORDER BY COUNT(o.Id) DESC",
                        new { ProductId = productId, target.BrandId });

                    eligible = brandCandidates
                        .Select(c => new ScoredProduct(c, 0, c.CategoryId == target.CategoryId))
                        .ToList();
                }

                return eligible
                    .OrderByDescending(x => x.SameCategory)
                    .ThenByDescending(x => x.Score)
                    .Take(10)
                    .Select(x =>
                    {
                        var unitPrice = UnitPriceCalculator.Calculate(x.Product.LowestPrice, x.Product.SizeValue, x.Product.SizeUnit, x.Product.PackQuantity);
                        return new SimilarProductRow(
                            x.Product.Id,
                            x.Product.CanonicalTitle,
                            x.Product.Brand,
                            x.Product.LowestPrice,
                            x.Product.OfferCount,
                            x.Product.ImageUrl,
                            x.SameCategory,
                            unitPrice?.Amount,
                            unitPrice?.Label
                        );
                    })
                    .ToList();
            });

            if (result is null || result.Count == 0) return Results.NotFound();
            return Results.Ok(result);
        });

        app.MapGet("/api/products", async (
            string? q,
            string? brand,
            string? category,
            string? sort,
            decimal? minPrice,
            decimal? maxPrice,
            string[]? attr,
            bool? onSale,
            int? page,
            int? pageSize,
            LiveSearchService liveSearchService,
            [FromServices] IConnectionMultiplexer? redis,
            [FromServices] IConfiguration configuration) =>
        {
            int currentPage = Math.Max(1, page ?? 1);
            int limit = Math.Clamp(pageSize ?? 24, 1, 100);
            int offset = (currentPage - 1) * limit;

            using var db = new SqlConnection(connectionString);

            var (whereClause, parameters, matchingCatIds) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
            ProductFilterHelpers.AppendBrandFilter(brand ?? "", ref whereClause, parameters);
            ProductFilterHelpers.AppendAttrGroupFilters(ProductFilterHelpers.ParseAttrGroups(attr), ref whereClause, parameters);

            var categoryTier = matchingCatIds is { Count: > 0 }
                ? "WHEN p.CategoryId IN @MatchingCatIds THEN 1"
                : "";
            var relevanceOrder = $@"
                CASE
                    WHEN p.Name = @Query THEN 0
                    {categoryTier}
                    WHEN p.Name LIKE (@Query + ' %') OR p.Name LIKE ('% ' + @Query) THEN 2
                    WHEN (' ' + p.Name + ' ') LIKE ('% ' + @Query + ' %') THEN 3
                    WHEN EXISTS (SELECT 1 FROM Brands rb WHERE rb.Id = p.BrandId AND (' ' + rb.Name + ' ') LIKE ('% ' + @Query + ' %')) THEN 4
                    ELSE 5
                END, LEN(p.Name) ASC, p.Name ASC";

            var hasQuery = !string.IsNullOrWhiteSpace(q);
            var orderBy = sort switch
            {
                "price_desc" => "(SELECT MIN(o6.Price) FROM Offers o6 WHERE o6.ProductId = p.Id) DESC",
                "price_asc" => "(SELECT MIN(o6.Price) FROM Offers o6 WHERE o6.ProductId = p.Id) ASC",
                "name_desc" => "p.Name DESC",
                "newest" => "p.CreatedAt DESC",
                _ when hasQuery => relevanceOrder,
                _ => "p.Name ASC"
            };

            var countSql = $"SELECT COUNT(*) FROM Products p {whereClause}";

            var idSql = $@"
                SELECT p.Id
                FROM Products p
                {whereClause}
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            parameters.Add("Offset", offset);
            parameters.Add("PageSize", limit);

            var totalCount = await db.ExecuteScalarAsync<int>(countSql, parameters, commandTimeout: 30);
            var pageIds = (await db.QueryAsync<int>(idSql, parameters, commandTimeout: 30)).ToList();

            if (!string.IsNullOrWhiteSpace(q) && totalCount > 0 && redis != null && redis.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var rDb = redis.GetDatabase();
                        var today = DateTime.Now.ToString("yyyy-MM-dd");
                        var key = $"popular_searches:{today}";
                        var term = q.Trim().ToLowerInvariant();
                        await rDb.SortedSetIncrementAsync(key, term, 1);
                        await rDb.KeyExpireAsync(key, TimeSpan.FromDays(8));
                    }
                    catch
                    {
                        // Fail-safe for Redis recording
                    }
                });
            }

            int minResultsThreshold = configuration.GetValue<int?>("LiveSearch:MinResultsThreshold") ?? 20;

            if (totalCount < minResultsThreshold && !string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(brand))
            {
                await liveSearchService.ExecuteAsync(q.Trim());

                totalCount = await db.ExecuteScalarAsync<int>(countSql, parameters, commandTimeout: 30);
                pageIds = (await db.QueryAsync<int>(idSql, parameters, commandTimeout: 30)).ToList();
            }

            var masterProducts = Enumerable.Empty<ProductListRow>();
            if (pageIds.Count > 0)
            {
                var pageRows = await db.QueryAsync<ProductListRow>(@"
                    SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                           MIN(o.Price) AS LowestPrice,
                           COUNT(o.Id) AS OfferCount,
                           p.ImageUrl,
                           p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                           MAX(o.RegularPrice) AS OldPrice
                    FROM Products p
                    JOIN Brands b ON b.Id = p.BrandId
                    JOIN Offers o ON o.ProductId = p.Id
                    WHERE p.Id IN @PageIds
                    GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId",
                    new { PageIds = pageIds }, commandTimeout: 30);

                var rank = pageIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
                masterProducts = pageRows.OrderBy(mp => rank[mp.Id]);
            }

            var items = masterProducts.Select(mp =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(mp.LowestPrice, mp.SizeValue, mp.SizeUnit, mp.PackQuantity);
                var discountPercent = mp.OldPrice.HasValue && mp.OldPrice > mp.LowestPrice
                    ? Math.Round((mp.OldPrice.Value - mp.LowestPrice) / mp.OldPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    mp.CanonicalTitle,
                    mp.Brand,
                    mp.LowestPrice,
                    mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.OldPrice : null,
                    DiscountPercent = discountPercent
                };
            });

            int totalPages = (int)Math.Ceiling((double)totalCount / limit);

            return Results.Ok(new
            {
                items,
                totalCount,
                page = currentPage,
                pageSize = limit,
                totalPages
            });
        });

        app.MapGet("/api/products/{productId}/price-history", async (int productId) =>
        {
            using var db = new SqlConnection(connectionString);
            var history = await db.QueryAsync(@"
                SELECT oph.Price, oph.RecordedAt, s.Name AS SellerName
                FROM OffersPriceHistory oph
                JOIN Offers o ON o.Id = oph.OfferId
                JOIN Sellers s ON s.Id = o.SellerId
                WHERE o.ProductId = @ProductId
                ORDER BY oph.RecordedAt ASC",
                new { ProductId = productId });

            var grouped = history
                .GroupBy(h => Convert.ToDateTime(h.RecordedAt).ToString("yyyy-MM-dd"))
                .Select(g => new
                {
                    Date = g.Key,
                    MinPrice = g.Min(x => (decimal)x.Price),
                    AvgPrice = Math.Round(g.Average(x => (decimal)x.Price), 2),
                    Offers = g.Select(x => new { SellerName = (string)x.SellerName, Price = (decimal)x.Price })
                })
                .ToList();

            return Results.Ok(grouped);
        });
    }
}

public record PriceHistoryDbRow(decimal Price, DateTime RecordedAt, string SellerName);
public record CurrentOfferDbRow(decimal Price, string SellerName);
public record OfferDetailDto(string SellerName, decimal Price);
public class PriceHistoryGroupDto
{
    public string Date { get; set; } = string.Empty;
    public decimal MinPrice { get; set; }
    public decimal AvgPrice { get; set; }
    public List<OfferDetailDto> Offers { get; set; } = new();
}

public record OfferRow(string CanonicalTitle, string Source, decimal Price, string? ImageUrl,
    string ProductUrl, decimal? SizeValue, string? SizeUnit, int PackQuantity);

public record ProductListRow(int Id, string CanonicalTitle, string? Brand, decimal LowestPrice,
    int OfferCount, string? ImageUrl, decimal? SizeValue, string? SizeUnit, int PackQuantity, int? CategoryId,
    decimal? OldPrice);

public record TargetProductRow(string Name, int? CategoryId, int? BrandId, string? SectorName);

public record SimilarProductRow(int Id, string CanonicalTitle, string? Brand, decimal LowestPrice, int OfferCount, string? ImageUrl, bool SameCategory, decimal? UnitPrice, string? UnitLabel);

public record ScoredProduct(ProductListRow Product, double Score, bool SameCategory);
