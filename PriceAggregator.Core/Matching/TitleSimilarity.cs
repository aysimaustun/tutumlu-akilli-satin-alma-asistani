namespace PriceAggregator.Core.Matching;

// Shared title-similarity scoring used both by MatchingService (deciding whether a newly
// scraped offer is the same physical product, threshold ~0.80) and the Api's "similar
// products" endpoint (a looser threshold, ~0.60, for suggesting related-but-different items).
public static class TitleSimilarity
{
    private static readonly HashSet<string> UnitWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ml", "lt", "l", "g", "gr", "kg", "adet", "cc", "pet"
    };

    private static readonly HashSet<string> DescriptorFillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pet", "sise", "su", "dogal", "kaynak", "sade"
    };

    // Words that mark a genuinely different product variant (flavor, scent, dietary format,
    // color, etc.) rather than a harmless naming difference. When one title has one of these
    // that the other doesn't, containment must NOT auto-match them — see Calculate. Not
    // exhaustive by construction (token overlap alone can't tell "harmless extra word" from
    // "distinguishing extra word" without a curated list); extend this as real cases turn up,
    // e.g. via the /api/admin/review-product-matches findings.
    private static readonly HashSet<string> DistinguishingModifierWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Flavor / scent
        "cikolatali", "cikolata", "findikli", "findik", "cilekli", "cilek", "muzlu", "muz",
        "portakalli", "portakal", "limonlu", "limon", "vanilyali", "vanilya", "karamelli", "karamel",
        "fistikli", "fistik", "tarcinli", "tarcin", "balli", "bal", "meyveli", "meyve",
        "kayisili", "kayisi", "seftalili", "seftali", "ananasli", "ananas", "nane", "naneli",
        "misk", "lavanta", "okyanus", "lotus",
        // Dietary / format qualifiers
        "sekersiz", "seker", "kafeinsiz", "kafein", "light", "yagli", "yagsiz", "glutensiz", "gluten",
        "laktozsuz", "laktoz", "tuzsuz", "tuz", "diyet", "fitness", "protein", "proteinli", "zero",
        // Common variant/color markers
        "beyaz", "siyah", "mavi", "kirmizi", "yesil", "sari", "mor", "pembe", "gri"
    };

    private static HashSet<string> Tokenize(string text, string? brand)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();

        var normalized = TurkishNormalizer.Normalize(text);
        var cleaned = new string(normalized.Select(c =>
            char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray());

        var brandTokens = string.IsNullOrWhiteSpace(brand)
            ? new HashSet<string>()
            : TurkishNormalizer.Normalize(brand).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Where(w => !brandTokens.Contains(w))
            .Where(w => !UnitWords.Contains(w))
            .Where(w => !DescriptorFillerWords.Contains(w))
            .Where(w => !w.All(char.IsDigit))
            .ToHashSet();
    }

    public static double Calculate(string title1, string title2, string? brand)
    {
        var tokens1 = Tokenize(title1, brand);
        var tokens2 = Tokenize(title2, brand);

        if (tokens1.Count == 0 && tokens2.Count == 0) return 1.0;
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0.0;

        // Full containment: every token of the smaller set has a fuzzy match in the larger set,
        // AND whatever's left over in the larger set carries no distinguishing meaning. Handles
        // "official name" vs "shorter alias" cases (e.g. "Penne Rigate (Kalem)" vs "Kalem" —
        // leftover "Penne"/"Rigate" are harmless format words) without being fooled by a real
        // variant difference (e.g. "X Zero" vs "X Zero Kafeinsiz" — leftover "Kafeinsiz" is a
        // DistinguishingModifierWord, so this must NOT auto-match just because "Zero" contains).
        bool oneWayContainment(HashSet<string> smaller, HashSet<string> larger)
        {
            var leftover = new HashSet<string>(larger, StringComparer.OrdinalIgnoreCase);
            foreach (var s in smaller)
            {
                var match = leftover.FirstOrDefault(l => FuzzyMatcher.TokensMatch(s, l));
                if (match is null) return false;
                leftover.Remove(match);
            }
            return !leftover.Any(DistinguishingModifierWords.Contains);
        }

        if (tokens1.Count <= tokens2.Count && oneWayContainment(tokens1, tokens2))
            return 1.0;
        if (tokens2.Count < tokens1.Count && oneWayContainment(tokens2, tokens1))
            return 1.0;

        var remaining = new HashSet<string>(tokens2);
        int matches = 0;

        foreach (var t1 in tokens1)
        {
            var match = remaining.FirstOrDefault(t2 => FuzzyMatcher.TokensMatch(t1, t2));
            if (match != null)
            {
                matches++;
                remaining.Remove(match);
            }
        }

        int union = tokens1.Count + tokens2.Count - matches;
        return (double)matches / union;
    }
}
