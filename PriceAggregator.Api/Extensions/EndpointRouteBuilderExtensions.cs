using PriceAggregator.Api.Endpoints;

namespace PriceAggregator.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapAuthEndpoints();
        app.MapFavoritesEndpoints();
        app.MapBrandEndpoints(connectionString);
        app.MapCategoryEndpoints(connectionString);
        app.MapAttributeEndpoints(connectionString);
        app.MapSearchEndpoints(connectionString);
        app.MapAdminEndpoints(connectionString);
        app.MapProductEndpoints(connectionString);

        return app;
    }
}
