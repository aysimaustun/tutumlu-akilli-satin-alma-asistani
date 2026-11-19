using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/categories", async (ICacheService cache) =>
        {
            var categories = await cache.GetOrSetAsync<List<CategoryCacheRow>>("categories_all", TimeSpan.FromHours(6), async () =>
            {
                using var db = new SqlConnection(connectionString);
                return (await db.QueryAsync<CategoryCacheRow>(@"
                    SELECT Id, Name, Slug, ParentCategoryId
                    FROM Categories
                    ORDER BY ParentCategoryId ASC, Name ASC")).ToList();
            });
            return Results.Ok(categories);
        });

        app.MapGet("/api/search-categories", async (string? q) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.Ok(Array.Empty<object>());

            var term = q.Trim();
            var likeTerm = $"%{term}%";

            using var db = new SqlConnection(connectionString);

            var matchingCatIds = (await db.QueryAsync<int>(@"
                WITH CatTree AS (
                    SELECT Id FROM Categories WHERE Name LIKE @LikeTerm
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CatTree ct ON c.ParentCategoryId = ct.Id
                )
                SELECT DISTINCT Id FROM CatTree", new { LikeTerm = likeTerm }, commandTimeout: 15)).ToList();

            var sql = @"
                SELECT p.CategoryId, COUNT(DISTINCT p.Id) AS ProductCount
                FROM Products p
                JOIN Brands b ON b.Id = p.BrandId
                WHERE p.CategoryId IS NOT NULL";

            var parameters = new DynamicParameters();
            parameters.Add("LikeTerm", likeTerm);

            if (matchingCatIds.Count > 0)
            {
                sql += " AND (p.Name LIKE @LikeTerm OR b.Name LIKE @LikeTerm OR p.CategoryId IN @MatchingCatIds)";
                parameters.Add("MatchingCatIds", matchingCatIds);
            }
            else
            {
                sql += " AND (p.Name LIKE @LikeTerm OR b.Name LIKE @LikeTerm)";
            }

            sql += " GROUP BY p.CategoryId";

            var rows = await db.QueryAsync(sql, parameters, commandTimeout: 15);
            return Results.Ok(rows);
        });
    }
}

public record CategoryCacheRow(int Id, string Name, string Slug, int? ParentCategoryId);
