using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core;

namespace PriceAggregator.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        var group = app.MapGroup("/api/admin");

        group.MapPost("/merge-duplicate-categories", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var merged = await CategoryMerger.MergeDuplicatesAsync(db);
            return Results.Ok(new { merged });
        });

        group.MapPost("/merge-synonym-categories", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var merged = await CategorySynonymMerger.MergeSynonymsAsync(db);
            return Results.Ok(new { merged });
        });

        group.MapPost("/clean-empty-categories", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var removed = await CategoryCleaner.RemoveEmptyAsync(db);
            return Results.Ok(new { removed });
        });

        group.MapPost("/clean-nonfood-attribute-tags", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var removed = await FoodPropertyGate.RemoveExistingNonFoodTagsAsync(db);
            return Results.Ok(new { removed });
        });

        group.MapGet("/category-health", async () =>
        {
            using var db = new SqlConnection(connectionString);

            var knownSectorNames = new[] { SectorClassifier.MarketSectorName, SectorClassifier.MobilyaSectorName, SectorClassifier.KozmetikSectorName };

            var unreachableSectors = await db.QueryAsync<(int Id, string Name, int ProductCount)>(@"
                SELECT c.Id, c.Name, COUNT(p.Id) AS ProductCount
                FROM Categories c
                LEFT JOIN Categories descendant ON descendant.SectorId = c.Id
                LEFT JOIN Products p ON p.CategoryId = descendant.Id
                WHERE c.ParentCategoryId IS NULL AND c.Name NOT IN @KnownSectorNames
                GROUP BY c.Id, c.Name",
                new { KnownSectorNames = knownSectorNames });

            var rootStuckCounts = await db.QueryAsync<(int CategoryId, string SectorName, int ProductCount)>(@"
                SELECT c.Id AS CategoryId, c.Name AS SectorName, COUNT(p.Id) AS ProductCount
                FROM Categories c
                JOIN Products p ON p.CategoryId = c.Id
                WHERE c.ParentCategoryId IS NULL
                GROUP BY c.Id, c.Name");

            return Results.Ok(new
            {
                UnreachableSectors = unreachableSectors,
                ProductsStuckAtSectorRoot = rootStuckCounts
            });
        });

        group.MapPost("/reclassify-categories", async () =>
        {
            using var db = new SqlConnection(connectionString);

            var uncategorized = await db.QueryAsync<(int Id, string CanonicalTitle, string? Source)>(@"
                SELECT p.Id, p.Name AS CanonicalTitle,
                       (SELECT TOP 1 s.Name FROM Offers o JOIN Sellers s ON s.Id = o.SellerId WHERE o.ProductId = p.Id) AS Source
                FROM Products p WHERE p.CategoryId IS NULL");

            int updated = 0;
            foreach (var product in uncategorized)
            {
                if (product.Source is null) continue;

                var categoryId = await CategoryResolver.ResolveAsync(db, new ProductDto { Title = product.CanonicalTitle }, product.Source);
                if (categoryId is null) continue;

                await db.ExecuteAsync(
                    "UPDATE Products SET CategoryId = @CategoryId WHERE Id = @Id",
                    new { CategoryId = categoryId, product.Id });
                updated++;
            }

            return Results.Ok(new { updated });
        });

        group.MapPost("/reclassify-category/{categoryId}", async (int categoryId) =>
        {
            using var db = new SqlConnection(connectionString);

            var products = await db.QueryAsync<(int Id, string Name)>(
                "SELECT Id, Name FROM Products WHERE CategoryId = @CategoryId",
                new { CategoryId = categoryId });

            int moved = 0;
            foreach (var product in products)
            {
                var newCategoryId = await CategoryResolver.ResolveAsync(db, new ProductDto { Title = product.Name }, "migros");
                if (newCategoryId is not null && newCategoryId != categoryId)
                {
                    await db.ExecuteAsync(
                        "UPDATE Products SET CategoryId = @CategoryId WHERE Id = @Id",
                        new { CategoryId = newCategoryId, Id = product.Id });
                    moved++;
                }
            }

            return Results.Ok(new { moved, total = products.Count() });
        });

        group.MapGet("/review-product-matches", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var suspects = await ProductMatchReviewer.FindSuspectsAsync(db);
            return Results.Ok(new { count = suspects.Count, suspects });
        });

        group.MapPost("/fix-product-matches", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var outcomes = await ProductMatchReviewer.FixSuspectsAsync(db);
            return Results.Ok(new
            {
                total = outcomes.Count,
                movedToExisting = outcomes.Count(o => o.Action == "Moved to existing product"),
                createdNew = outcomes.Count(o => o.Action == "Created new product"),
                skipped = outcomes.Count(o => o.Action.StartsWith("Skipped")),
                outcomes
            });
        });

        group.MapPost("/reparse-sizes", async () =>
        {
            using var db = new SqlConnection(connectionString);
            var products = await db.QueryAsync<(int Id, string CanonicalTitle)>(
                "SELECT Id, Name AS CanonicalTitle FROM Products");

            int updated = 0;
            foreach (var product in products)
            {
                var (size, unit, packQty) = SizeParser.ExtractSize(product.CanonicalTitle);
                var rows = await db.ExecuteAsync(@"
                    UPDATE Products SET SizeValue = @Size, SizeUnit = @Unit, PackQuantity = @PackQty
                    WHERE Id = @Id
                    AND (ISNULL(SizeValue, -1) <> ISNULL(@Size, -1)
                         OR ISNULL(SizeUnit, '') <> ISNULL(@Unit, '')
                         OR PackQuantity <> @PackQty)",
                    new { Id = product.Id, Size = size, Unit = unit, PackQty = packQty });
                updated += rows;
            }

            return Results.Ok(new { updated, total = products.Count() });
        });
    }
}
