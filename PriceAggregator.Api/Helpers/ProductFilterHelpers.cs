using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Api.Helpers;

public static class ProductFilterHelpers
{
    public static string WordBoundaryLike(string column) => $"(' ' + {column} + ' ') LIKE ('% ' + @Query + ' %')";

    public static async Task<(string WhereClause, DynamicParameters Parameters, List<int>? MatchingCatIds)> BuildBaseProductFilterAsync(
        SqlConnection db,
        string? category,
        string? q,
        decimal? minPrice,
        decimal? maxPrice,
        bool? onSale)
    {
        var whereClause = " WHERE EXISTS (SELECT 1 FROM Offers o WHERE o.ProductId = p.Id)";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryTreeIds = (await db.QueryAsync<int>(@"
                WITH CatTree AS (
                    SELECT Id FROM Categories WHERE Slug = @CategorySlug
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CatTree ct ON c.ParentCategoryId = ct.Id
                )
                SELECT DISTINCT Id FROM CatTree", new { CategorySlug = category }, commandTimeout: 15)).ToList();

            if (categoryTreeIds.Count > 0)
            {
                whereClause += " AND p.CategoryId IN @CategoryTreeIds";
                parameters.Add("CategoryTreeIds", categoryTreeIds);
            }
            else
            {
                whereClause += " AND 1 = 0";
            }
        }

        List<int>? matchingCatIds = null;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();

            matchingCatIds = (await db.QueryAsync<int>(@"
                WITH CatTree AS (
                    SELECT Id FROM Categories WHERE Name = @Query
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CatTree ct ON c.ParentCategoryId = ct.Id
                )
                SELECT DISTINCT Id FROM CatTree", new { Query = term }, commandTimeout: 15)).ToList();

            var nameMatch = WordBoundaryLike("p.Name");
            var brandMatch = $"EXISTS (SELECT 1 FROM Brands b WHERE b.Id = p.BrandId AND {WordBoundaryLike("b.Name")})";
            if (matchingCatIds.Count > 0)
            {
                whereClause += $" AND ({nameMatch} OR {brandMatch} OR p.CategoryId IN @MatchingCatIds)";
                parameters.Add("MatchingCatIds", matchingCatIds);
            }
            else
            {
                whereClause += $" AND ({nameMatch} OR {brandMatch})";
            }
            parameters.Add("Query", term);
        }

        if (onSale == true)
        {
            whereClause += " AND EXISTS (SELECT 1 FROM Offers o3 WHERE o3.ProductId = p.Id AND o3.RegularPrice IS NOT NULL AND o3.RegularPrice > o3.Price)";
        }

        if (minPrice.HasValue)
        {
            whereClause += " AND NOT EXISTS (SELECT 1 FROM Offers o4 WHERE o4.ProductId = p.Id AND o4.Price < @MinPrice)";
            parameters.Add("MinPrice", minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            whereClause += " AND EXISTS (SELECT 1 FROM Offers o5 WHERE o5.ProductId = p.Id AND o5.Price <= @MaxPrice)";
            parameters.Add("MaxPrice", maxPrice.Value);
        }

        return (whereClause, parameters, matchingCatIds);
    }

    public static void AppendBrandFilter(string brand, ref string whereClause, DynamicParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(brand))
        {
            whereClause += " AND EXISTS (SELECT 1 FROM Brands b WHERE b.Id = p.BrandId AND b.Name = @Brand)";
            parameters.Add("Brand", brand);
        }
    }

    public static List<(string Name, List<string> Values)> ParseAttrGroups(string[]? attr)
    {
        if (attr is null || attr.Length == 0) return [];
        return attr
            .Select(a => a.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], parts => parts[1])
            .Select(g => (g.Key, g.ToList()))
            .ToList();
    }

    public static void AppendAttrGroupFilters(List<(string Name, List<string> Values)> groups, ref string whereClause, DynamicParameters parameters)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            whereClause += $@" AND EXISTS (
                SELECT 1 FROM ProductAttributes pa{i}
                WHERE pa{i}.ProductId = p.Id AND pa{i}.AttributeName = @AttrName{i} AND pa{i}.AttributeValue IN @AttrValues{i})";
            parameters.Add($"AttrName{i}", groups[i].Name);
            parameters.Add($"AttrValues{i}", groups[i].Values);
        }
    }
}
