using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class BrandEndpoints
{
    public static void MapBrandEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/brands", async (string? category, string? q, string[]? attr, decimal? minPrice, decimal? maxPrice, bool? onSale, ICacheService cache) =>
        {
            async Task<List<string>> FetchAsync()
            {
                using var db = new SqlConnection(connectionString);
                var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                ProductFilterHelpers.AppendAttrGroupFilters(ProductFilterHelpers.ParseAttrGroups(attr), ref whereClause, parameters);

                var sql = $@"
                    SELECT DISTINCT b.Name
                    FROM Brands b
                    JOIN Products p ON p.BrandId = b.Id
                    {whereClause}
                    AND b.Name IS NOT NULL AND b.Name <> ''
                    ORDER BY b.Name";

                return (await db.QueryAsync<string>(sql, parameters, commandTimeout: 30)).ToList();
            }

            var hasNarrowingFilters = !string.IsNullOrWhiteSpace(q) || (attr?.Length > 0) || minPrice.HasValue || maxPrice.HasValue || onSale == true;
            if (!hasNarrowingFilters)
            {
                var cacheKey = $"brands_{category ?? "all"}";
                var result = await cache.GetOrSetAsync(cacheKey, TimeSpan.FromHours(6), FetchAsync);
                return Results.Ok(result ?? new List<string>());
            }

            return Results.Ok(await FetchAsync());
        });
    }
}
