namespace PriceAggregator.Core.Favorites;

// Mirrors ProductEndpoints.ProductListRow so favorited products render through the same
// renderProductCard() on the frontend as the regular product grid.
public record FavoriteProductRow(int Id, string CanonicalTitle, string? Brand, decimal LowestPrice,
    int OfferCount, string? ImageUrl, decimal? SizeValue, string? SizeUnit, int PackQuantity,
    decimal? OldPrice);
