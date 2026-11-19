namespace PriceAggregator.Core.Categorization;

// Migros/MacroCenter/Mion run on the same platform and already agree on one Market
// department taxonomy (see CategoryResolver.ResolveMarketAsync), which is why that method
// used to trust every source's Root/Parent/Leaf breadcrumb literally. HakmarExpress and
// MarketFiyati both break that assumption: their own breadcrumb wording for the very same
// real department differs ("Süt & Kahvaltılık"/"Süt Ürünleri ve Kahvaltılık" vs the existing
// "Süt Ürünleri", "Maden Suyu" vs "Su", "Kahve & Toz İçecek" vs "Kahve") and FuzzyMatcher's
// tolerance isn't enough to catch a real wording difference like that — left alone, every
// mismatched name created its own redundant branch sitting next to the real department on
// every scrape run (confirmed live: a full scrape run on 2026-08-05 created "Süt Ürünleri ve
// Kahvaltılık", "Çay ve Bitki Çayları", "Maden Suyu" and "Kahve & Toz İçecek" as duplicates of
// existing nodes before these entries were added; sql cleanup merged the four that had already
// been created). Applied to Root/Parent/Category breadcrumb text in ResolveMarketAsync before
// it reaches FindOrCreateAsync.
//
// Deliberately small: only covers names actually observed to diverge from the existing tree.
// A source's leaf name is left as-is when unmapped — that's real per-store granularity, not
// the kind of duplicate-department bloat this exists to prevent.
public static class MarketCategorySynonyms
{
    private static readonly Dictionary<string, string?> Map = BuildMap();

    // Null means "this breadcrumb segment is pure umbrella noise, drop it" — the level
    // beneath it already carries the real, matching department name on its own.
    public static string? Canonicalize(string name)
    {
        var normalized = TurkishNormalizer.Normalize(name);
        return Map.TryGetValue(normalized, out var canonical) ? canonical : name;
    }

    private static Dictionary<string, string?> BuildMap()
    {
        (string Source, string? Canonical)[] pairs =
        [
            ("Süt & Kahvaltılık", "Süt Ürünleri"),
            ("Süt Ürünleri ve Kahvaltılık", "Süt Ürünleri"),
            ("Süt, Kahvaltılık", "Süt Ürünleri"),
            ("Et & Et Ürünleri", "Et ve Şarküteri"),
            ("Çay ve Bitki Çayları", "Çay"),
            ("Maden Suyu", "Su"),
            ("Kahve & Toz İçecek", "Kahve"),

            // "Atıştırmalık" department: each pair below is the same real snack type split
            // across two sources — one names just the item, the other bundles in a close
            // relative ("ve Gofret"/"ve Şekerleme"/"& Bar"/"ve Kraker") that isn't its own
            // department anywhere else in the tree, just a wording habit of that source.
            ("Bisküvi ve Gofret", "Bisküvi"),
            ("Çikolata ve Şekerleme", "Çikolata"),
            ("Çikolata, Gofret & Bar", "Çikolata"),
            ("Cips ve Kraker", "Cips"),

            // "Kaşar Peyniri" already says Peyniri in the parent — a child repeating it
            // ("Taze Kaşar Peyniri") is the same product as the existing "Taze Kaşar" leaf,
            // just a more verbose source wording.
            ("Taze Kaşar Peyniri", "Taze Kaşar"),

            // Hakmar's broadest bucket — its own children are "Temel Gıda"/"İçecek"/
            // "Atıştırmalık" etc., which already match our real departments by name on their
            // own. Keeping this level would just nest those real departments one level below
            // a redundant, never-otherwise-seen parent.
            ("Kuru Gıda & İçecek", null),
        ];

        var map = new Dictionary<string, string?>();
        foreach (var (source, canonical) in pairs)
            map[TurkishNormalizer.Normalize(source)] = canonical;
        return map;
    }
}
