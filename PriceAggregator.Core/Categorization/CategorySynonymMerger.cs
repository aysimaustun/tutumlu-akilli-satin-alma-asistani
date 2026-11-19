using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Categorization;

// Complements CategoryMerger: that one catches near-duplicates FuzzyMatcher recognizes as
// the same wording (plural/spelling variants). This one catches the other kind of duplicate —
// two categories that mean the same real department but are worded too differently for
// FuzzyMatcher to ever bridge ("Süt, Kahvaltılık" vs "Süt Ürünleri"), which is exactly what
// MarketCategorySynonyms/CosmeticsCategorySynonyms already know how to canonicalize for
// *new* scrapes via CategoryResolver. This just re-applies those same dictionaries to
// categories that were already created before the relevant entry existed, merging each one
// into its canonical-named sibling. A category with no canonical-named sibling yet (nothing
// to merge into) is left alone — CategoryResolver will start reusing/creating the canonical
// name correctly on the next scrape either way.
public static class CategorySynonymMerger
{
    public static async Task<int> MergeSynonymsAsync(SqlConnection db)
    {
        var rows = (await db.QueryAsync<(int Id, string Name, int? ParentCategoryId, string? SectorName)>(@"
            SELECT c.Id, c.Name, c.ParentCategoryId, sector.Name AS SectorName
            FROM Categories c
            LEFT JOIN Categories sector ON sector.Id = c.SectorId
            WHERE c.ParentCategoryId IS NOT NULL")).ToList();

        int merged = 0;

        foreach (var row in rows)
        {
            var canonical = row.SectorName switch
            {
                SectorClassifier.MarketSectorName => MarketCategorySynonyms.Canonicalize(row.Name),
                SectorClassifier.KozmetikSectorName => CosmeticsCategorySynonyms.Canonicalize(row.Name),
                SectorClassifier.MobilyaSectorName => FurnitureLeafSynonyms.Canonicalize(row.Name),
                _ => null,
            };

            if (canonical is null || canonical == row.Name) continue;

            var targetId = await db.QuerySingleOrDefaultAsync<int?>(
                @"SELECT Id FROM Categories
                  WHERE Name = @Canonical
                  AND ((ParentCategoryId = @ParentId) OR (@ParentId IS NULL AND ParentCategoryId IS NULL))
                  AND Id <> @Id",
                new { Canonical = canonical, ParentId = row.ParentCategoryId, row.Id });

            if (targetId is null) continue;

            await db.ExecuteAsync(
                "UPDATE Products SET CategoryId = @TargetId WHERE CategoryId = @Id",
                new { TargetId = targetId, row.Id });

            await db.ExecuteAsync(
                "UPDATE Categories SET ParentCategoryId = @TargetId WHERE ParentCategoryId = @Id",
                new { TargetId = targetId, row.Id });

            await db.ExecuteAsync(
                "DELETE FROM Categories WHERE Id = @Id",
                new { row.Id });

            merged++;
        }

        return merged;
    }
}
