using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class AttributeEndpoints
{
    public static void MapAttributeEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/attributes", async (string? category, string? q, string? brand, string[]? attr, decimal? minPrice, decimal? maxPrice, bool? onSale, ICacheService cache) =>
        {
            async Task<List<AttributeGroupRow>> FetchAsync()
            {
                using var db = new SqlConnection(connectionString);

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var isSectorRoot = await db.QueryFirstOrDefaultAsync<bool?>(
                        "SELECT CAST(CASE WHEN ParentCategoryId IS NULL THEN 1 ELSE 0 END AS BIT) FROM Categories WHERE Slug = @Category",
                        new { Category = category });
                    if (isSectorRoot == true) return [];
                }

                var selectedGroups = ProductFilterHelpers.ParseAttrGroups(attr);

                async Task<IEnumerable<(string AttributeName, string AttributeValue, int ProductCount)>> QueryGroupAsync(string? onlyGroupName)
                {
                    var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                    ProductFilterHelpers.AppendBrandFilter(brand ?? "", ref whereClause, parameters);

                    var otherGroups = onlyGroupName is null
                        ? selectedGroups
                        : selectedGroups.Where(g => g.Name != onlyGroupName).ToList();
                    ProductFilterHelpers.AppendAttrGroupFilters(otherGroups, ref whereClause, parameters);

                    whereClause += " AND pa.AttributeName <> 'Adet'";
                    if (onlyGroupName is not null)
                    {
                        whereClause += " AND pa.AttributeName = @OnlyGroupName";
                        parameters.Add("OnlyGroupName", onlyGroupName);
                    }
                    else if (selectedGroups.Count > 0)
                    {
                        whereClause += " AND pa.AttributeName NOT IN @SelectedGroupNames";
                        parameters.Add("SelectedGroupNames", selectedGroups.Select(g => g.Name).ToList());
                    }

                    var sql = $@"
                        SELECT pa.AttributeName, pa.AttributeValue, COUNT(DISTINCT pa.ProductId) AS ProductCount
                        FROM ProductAttributes pa
                        JOIN Products p ON p.Id = pa.ProductId
                        {whereClause}
                        GROUP BY pa.AttributeName, pa.AttributeValue";

                    return await db.QueryAsync<(string AttributeName, string AttributeValue, int ProductCount)>(sql, parameters);
                }

                var rows = (await QueryGroupAsync(null)).ToList();
                foreach (var group in selectedGroups)
                    rows.AddRange(await QueryGroupAsync(group.Name));

                var (totalWhereClause, totalParameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                ProductFilterHelpers.AppendBrandFilter(brand ?? "", ref totalWhereClause, totalParameters);
                var totalProductCount = await db.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(DISTINCT p.Id) FROM Products p {totalWhereClause}", totalParameters);

                if (totalProductCount > 50)
                {
                    var selectedKeys = selectedGroups
                        .SelectMany(g => g.Values.Select(v => (Name: g.Name, Value: v)))
                        .ToHashSet();
                    var minCount = Math.Max(3, (int)Math.Ceiling(totalProductCount * 0.01));

                    rows = rows
                        .Where(r => r.ProductCount >= minCount || selectedKeys.Contains((r.AttributeName, r.AttributeValue)))
                        .ToList();
                }

                string[] volumeOrder = ["500 Ml ve altı", "500 Ml - 1 Litre", "1 - 2 Litre", "2 - 5 Litre", "5 Litre ve üzeri"];
                string[] weightOrder = ["250 G ve altı", "250 - 500 G", "500 G - 1 Kg", "1 - 2 Kg", "2 Kg ve üzeri"];

                int RankOf(string attributeName, string value)
                {
                    var order = attributeName switch { "Hacim" => volumeOrder, "Ağırlık" => weightOrder, _ => null };
                    if (order is null) return 0;
                    var i = Array.IndexOf(order, value);
                    return i >= 0 ? i : int.MaxValue;
                }

                return rows
                    .GroupBy(r => r.AttributeName)
                    .Select(g => new AttributeGroupRow(
                        g.Key,
                        g.OrderBy(r => RankOf(g.Key, r.AttributeValue))
                         .ThenBy(r => r.AttributeValue)
                         .Select(r => new AttributeValueRow(r.AttributeValue, r.ProductCount))
                         .ToList()
                    ))
                    .OrderBy(g => g.Name)
                    .ToList();
            }

            var hasNarrowingFilters = !string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(brand) || (attr?.Length > 0) || minPrice.HasValue || maxPrice.HasValue || onSale == true;
            if (!hasNarrowingFilters)
            {
                var cacheKey = $"attributes_{category ?? "all"}";
                var result = await cache.GetOrSetAsync(cacheKey, TimeSpan.FromHours(6), FetchAsync);
                return Results.Ok(result ?? new List<AttributeGroupRow>());
            }

            return Results.Ok(await FetchAsync());
        });
    }
}

public record AttributeGroupRow(string Name, List<AttributeValueRow> Values);
public record AttributeValueRow(string AttributeValue, int ProductCount);
