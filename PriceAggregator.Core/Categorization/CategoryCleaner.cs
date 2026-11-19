using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Categorization;

// Deletes Category rows that are genuinely dead weight: no products sit in them directly,
// and they have no children. This is distinct from CategoryMerger — a merge candidate has
// products/children that need to move somewhere; an empty category has nothing to preserve,
// so removal is safe on its own. Sector roots (ParentCategoryId IS NULL) are never touched
// even if briefly empty, since CategoryResolver recreates them as scrapes come in.
public static class CategoryCleaner
{
    public static async Task<int> RemoveEmptyAsync(SqlConnection db)
    {
        int totalRemoved = 0;

        // Removing a leaf can turn its own parent into a new empty leaf (e.g. a category
        // whose only child just got deleted), so this keeps sweeping until a pass removes
        // nothing — not just one pass.
        while (true)
        {
            var empty = (await db.QueryAsync<int>(@"
                SELECT c.Id
                FROM Categories c
                WHERE c.ParentCategoryId IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM Categories ch WHERE ch.ParentCategoryId = c.Id)
                AND NOT EXISTS (SELECT 1 FROM Products p WHERE p.CategoryId = c.Id)")).ToList();

            if (empty.Count == 0) break;

            await db.ExecuteAsync(
                "DELETE FROM Categories WHERE Id IN @Ids",
                new { Ids = empty });

            totalRemoved += empty.Count;
        }

        return totalRemoved;
    }
}
