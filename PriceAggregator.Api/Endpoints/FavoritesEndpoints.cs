using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PriceAggregator.Core.Favorites;

namespace PriceAggregator.Api.Endpoints;

public static class FavoritesEndpoints
{
    public static void MapFavoritesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var rows = await favoritesRepository.GetByUserIdAsync(userId.Value);
            var items = rows.Select(mp =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(mp.LowestPrice, mp.SizeValue, mp.SizeUnit, mp.PackQuantity);
                var discountPercent = mp.OldPrice.HasValue && mp.OldPrice > mp.LowestPrice
                    ? Math.Round((mp.OldPrice.Value - mp.LowestPrice) / mp.OldPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    mp.CanonicalTitle,
                    mp.Brand,
                    mp.LowestPrice,
                    mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.OldPrice : null,
                    DiscountPercent = discountPercent
                };
            });

            return Results.Ok(items);
        });

        group.MapPost("/{productId:int}", async (int productId, ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var added = await favoritesRepository.AddAsync(userId.Value, productId);
            if (!added) return Results.NotFound(new { error = "Ürün bulunamadı." });

            return Results.Ok();
        });

        group.MapDelete("/{productId:int}", async (int productId, ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await favoritesRepository.RemoveAsync(userId.Value, productId);
            return Results.Ok();
        });
    }

    private static int? GetUserId(ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return idClaim is not null && int.TryParse(idClaim, out var id) ? id : null;
    }
}
