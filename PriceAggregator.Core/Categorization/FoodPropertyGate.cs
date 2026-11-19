using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Categorization;

// Gates ProductPropertyTagger's dietary/lifestyle vocabulary (Organik, Vegan, Laktozsuz, ...)
// to the Market departments it can actually mean something in. That tagger runs on the title
// alone (see its own docs) and can't tell a food product from a non-food one on its own — but
// the Market sector also contains structurally non-food departments (cleaning supplies,
// electronics, toys), where e.g. "Laktozsuz" (lactose-free) or "Kafeinsiz" (caffeine-free) as
// a filter option is nonsense even on the rare title that happens to contain the word.
//
// Checked against the product's own resolved Category tree (not the source's raw breadcrumb
// text) so this stays correct regardless of which store's own department wording a product
// came in under — the canonical tree is the one place that naming drift has already been
// resolved (see MarketCategorySynonyms/CategorySynonymMerger).
public static class FoodPropertyGate
{
    // Denylist, not an allowlist: an unrecognized/new food department (or a product still
    // sitting on the bare sector root with no real category yet) keeps getting tagged as
    // before. Only departments confirmed structurally non-food are listed here.
    private static readonly HashSet<string> NonFoodDepartments = new(StringComparer.Ordinal)
    {
        "Deterjan & Temizlik",
        "Temizlik",
        "Elektronik",
        "Elektrikli Ev Aletleri",
        "Kitap, Kırtasiye, Oyuncak",
        "Ev & Yaşam",
    };

    public static async Task<bool> IsFoodRelatedAsync(SqlConnection db, int productId)
    {
        // Walks up from the product's own category to the deepest ancestor that still has a
        // parent — i.e. the Level 1 department directly under the sector root (Gıda, İçecek,
        // Temizlik, ...), regardless of how many levels deep the product's own category sits.
        var department = await db.QuerySingleOrDefaultAsync<string?>(@"
            ;WITH Ancestors AS (
                SELECT Id, Name, ParentCategoryId, 0 AS Depth
                FROM Categories
                WHERE Id = (SELECT CategoryId FROM Products WHERE Id = @ProductId)
                UNION ALL
                SELECT c.Id, c.Name, c.ParentCategoryId, a.Depth + 1
                FROM Categories c
                JOIN Ancestors a ON c.Id = a.ParentCategoryId
            )
            SELECT TOP 1 Name FROM Ancestors WHERE ParentCategoryId IS NOT NULL ORDER BY Depth DESC",
            new { ProductId = productId });

        return department is null || !NonFoodDepartments.Contains(department);
    }

    // One-off cleanup for 'Özellik' rows written before this gate existed — every product
    // under any of NonFoodDepartments' subtrees got scanned against ProductPropertyTagger's
    // dietary vocabulary regardless of department. Safe to run repeatedly: once these rows are
    // gone, the gate above stops new ones from ever being written for the same products.
    public static async Task<int> RemoveExistingNonFoodTagsAsync(SqlConnection db)
    {
        return await db.ExecuteAsync(@"
            ;WITH NonFoodTree AS (
                SELECT Id FROM Categories WHERE Name IN @DeptNames
                UNION ALL
                SELECT c.Id FROM Categories c JOIN NonFoodTree t ON c.ParentCategoryId = t.Id
            )
            DELETE pa
            FROM ProductAttributes pa
            JOIN Products p ON p.Id = pa.ProductId
            WHERE pa.AttributeName = 'Özellik'
            AND p.CategoryId IN (SELECT Id FROM NonFoodTree)",
            new { DeptNames = NonFoodDepartments.ToList() });
    }
}
