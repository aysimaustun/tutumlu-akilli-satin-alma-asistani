using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace PriceAggregator.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/search-suggestions", async (string? q) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length < 2)
                return Results.Ok(new { Brands = Array.Empty<object>(), Products = Array.Empty<object>() });

            var likeTerm = $"%{term}%";
            var parameters = new { Term = likeTerm };

            using var db = new SqlConnection(connectionString);

            var brands = await db.QueryAsync<string>(@"
                SELECT TOP 5 b.Name
                FROM Brands b
                WHERE b.Name LIKE @Term AND EXISTS (SELECT 1 FROM Products p WHERE p.BrandId = b.Id)
                ORDER BY LEN(b.Name), b.Name", parameters);

            var products = await db.QueryAsync(@"
                SELECT TOP 5 p.Id, p.Name AS CanonicalTitle, p.ImageUrl
                FROM Products p
                WHERE p.Name LIKE @Term AND EXISTS (SELECT 1 FROM Offers o WHERE o.ProductId = p.Id)
                ORDER BY LEN(p.Name), p.Name", parameters);

            return Results.Ok(new { Brands = brands, Products = products });
        });

        app.MapGet("/api/popular-searches", async ([FromServices] IConnectionMultiplexer? redis) =>
        {
            var defaultTrends = new[]
            {
                new { Term = "süt", Count = 10 },
                new { Term = "yağ", Count = 8 },
                new { Term = "deterjan", Count = 7 },
                new { Term = "kahve", Count = 5 },
                new { Term = "çay", Count = 4 },
                new { Term = "peynir", Count = 3 }
            };

            if (redis != null && redis.IsConnected)
            {
                try
                {
                    var db = redis.GetDatabase();
                    var keys = Enumerable.Range(0, 7)
                        .Select(i => (RedisKey)$"popular_searches:{DateTime.Now.AddDays(-i):yyyy-MM-dd}")
                        .ToArray();

                    var destKey = (RedisKey)"popular_searches:weekly";
                    var args = new RedisValue[2 + keys.Length];
                    args[0] = destKey.ToString();
                    args[1] = keys.Length;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        args[2 + i] = keys[i].ToString();
                    }
                    await db.ExecuteAsync("ZUNIONSTORE", args);
                    await db.KeyExpireAsync(destKey, TimeSpan.FromMinutes(10));

                    var entries = await db.SortedSetRangeByRankWithScoresAsync(destKey, 0, 9, Order.Descending);

                    if (entries.Length > 0)
                    {
                        var result = entries.Select(e => new
                        {
                            Term = e.Element.ToString(),
                            Count = (int)e.Score
                        }).ToList();

                        return Results.Ok(result);
                    }
                }
                catch
                {
                    // Fail-safe fallback to SQL Server archive
                }
            }

            try
            {
                using var sqlDb = new SqlConnection(connectionString);
                var sqlTrends = (await sqlDb.QueryAsync<(string Term, int Count)>(@"
                    SELECT TOP 10 SearchTerm AS Term, SUM(SearchCount) AS Count
                    FROM DailySearchTrends
                    WHERE SearchDate >= DATEADD(day, -7, GETDATE())
                    GROUP BY SearchTerm
                    ORDER BY SUM(SearchCount) DESC")).ToList();

                if (sqlTrends.Count > 0)
                {
                    return Results.Ok(sqlTrends.Select(t => new { t.Term, t.Count }));
                }
            }
            catch
            {
                // Fail-safe to defaultTrends
            }

            return Results.Ok(defaultTrends);
        });
    }
}
