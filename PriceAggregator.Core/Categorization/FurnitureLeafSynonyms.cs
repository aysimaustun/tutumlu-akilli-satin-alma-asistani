namespace PriceAggregator.Core.Categorization;

// CategoryResolver.ResolveMobilyaAsync nests a source's own real leaf name as a child of the
// shared canonical bucket (see that method's docs) so per-store granularity survives — but
// when two sources' leaf names mean the same real product type worded differently
// ("Sandalye Takımları" vs "Sandalye"), FuzzyMatcher's same-token-count rule doesn't bridge
// them and each becomes its own sibling. Same idea as MarketCategorySynonyms, deliberately
// just as small: only names actually observed to duplicate an existing leaf.
public static class FurnitureLeafSynonyms
{
    private static readonly Dictionary<string, string> Map = BuildMap();

    public static string Canonicalize(string name)
    {
        var normalized = TurkishNormalizer.Normalize(name);
        return Map.TryGetValue(normalized, out var canonical) ? canonical : name;
    }

    private static Dictionary<string, string> BuildMap()
    {
        (string Source, string Canonical)[] pairs =
        [
            ("Sandalye Takımları", "Sandalye"),
        ];

        var map = new Dictionary<string, string>();
        foreach (var (source, canonical) in pairs)
            map[TurkishNormalizer.Normalize(source)] = canonical;
        return map;
    }
}
