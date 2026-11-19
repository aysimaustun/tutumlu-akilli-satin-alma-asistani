using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using PriceAggregator.Api.Extensions;
using PriceAggregator.Core;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

// 1. Service Registrations
builder.Services.AddCorsPolicy();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRedisAndCaching(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHttpJsonOptions();

var app = builder.Build();

// 2. Middlewares
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// 3. Static Files & Frontend Configuration
var frontendPath = Path.Combine(builder.Environment.ContentRootPath, "Frontend");
if (!Directory.Exists(frontendPath))
{
    var altPath = Path.Combine(builder.Environment.ContentRootPath, "PriceAggregator.Api", "Frontend");
    if (Directory.Exists(altPath)) frontendPath = altPath;
}

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(frontendPath)
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(frontendPath)
});

// 4. Database Setup & Warmup
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

EnsureIndexesCreated(connectionString);
WarmupDatabase(connectionString);

// 5. Map API Endpoint Modules
app.MapApiEndpoints(connectionString);

app.Run();

static void EnsureIndexesCreated(string connStr)
{
    try
    {
        using var db = new SqlConnection(connStr);
        db.Execute(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_ProductId_Price')
                CREATE INDEX IX_Offers_ProductId_Price ON Offers(ProductId, Price) INCLUDE (RegularPrice);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_BrandId')
                CREATE INDEX IX_Products_BrandId ON Products(BrandId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_CategoryId')
                CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductAttributes_ProductId')
                CREATE INDEX IX_ProductAttributes_ProductId ON ProductAttributes(ProductId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_SectorId')
                CREATE INDEX IX_Categories_SectorId ON Categories(SectorId);

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DailySearchTrends')
            BEGIN
                CREATE TABLE DailySearchTrends (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SearchDate DATE NOT NULL,
                    SearchTerm NVARCHAR(100) NOT NULL,
                    SearchCount INT NOT NULL,
                    CreatedAt DATETIME DEFAULT GETDATE(),
                    CONSTRAINT UQ_DailySearchTrends_Date_Term UNIQUE (SearchDate, SearchTerm)
                );
                CREATE INDEX IX_DailySearchTrends_Date ON DailySearchTrends(SearchDate);
            END
        ");
    }
    catch
    {
        // Fail-safe if database isn't reachable on startup
    }
}

static void WarmupDatabase(string connStr)
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var db = new SqlConnection(connStr);
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products", commandTimeout: 60);
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Offers", commandTimeout: 60);
        }
        catch
        {
            // Fail-safe background warmup
        }
    });
}
