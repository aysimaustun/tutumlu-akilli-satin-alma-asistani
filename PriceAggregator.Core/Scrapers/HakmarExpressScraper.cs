using System.Linq;
using System.Text.Json;

namespace PriceAggregator.Core.Scrapers
{
    public class HakmarExpressScraper : IProductScraper
    {
        private readonly HttpClient _httpClient;

        public string Name => "hakmarexpress";
        public ScraperSector Sector => ScraperSector.Market;

        // Gerekli HTTP basliklarini (headers) ayarlayarak HttpClient nesnesini ilklendirir.
        public HakmarExpressScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Remove("Accept");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.8");
            _httpClient.DefaultRequestHeaders.Remove("Referer");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.hakmarexpress.com.tr/");
        }

        // Belirtilen arama terimine gore Hakmar Express API'sinden urunleri ceker ve dto listesi olarak doner.
        public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
        {
            var url = $"https://api.hakmarexpress.com.tr/api/home/slug/search?q={Uri.EscapeDataString(searchTerm)}";

            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            var results = new List<ProductDto>();

            // JSON icindeki 'product-list' bilesenini recursive yardimci ile bulalim
            JsonElement? productsArray = FindProductsArray(json.RootElement);

            // Eger urun listesi bulunamadiysa bos liste donelim
            if (productsArray == null)
            {
                return results;
            }

            // Urun kartlarini tek tek donerek DTO nesnesine esleyelim
            foreach (var card in productsArray.Value.EnumerateArray())
            {
                if (card.TryGetProperty("product", out var p))
                {
                    long externalId = p.GetProperty("id").GetInt64();
                    string title = p.GetProperty("name").GetString() ?? "";
                    string? imageUrl = p.TryGetProperty("imageUrl", out var imgProp) ? imgProp.GetString() : null;

                    string? brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.Object
                        ? (brandEl.TryGetProperty("name", out var brandName) ? brandName.GetString() : null)
                        : null;

                    string? barcode = p.TryGetProperty("barcode", out var barcodeEl) && barcodeEl.ValueKind == JsonValueKind.String
                        ? barcodeEl.GetString()
                        : null;

                    decimal price = p.GetProperty("price").GetDecimal();
                    decimal regularPrice = price;

                    if (p.TryGetProperty("oldPrice", out var oldPriceProp) && oldPriceProp.ValueKind != JsonValueKind.Null)
                    {
                        regularPrice = oldPriceProp.GetDecimal();
                    }

                    string slug = p.GetProperty("slug").GetString() ?? "";
                    string productUrl = $"https://www.hakmarexpress.com.tr/{slug}";

                    var (categoryName, parentCategoryName, rootCategoryName) = ParseCategory(p);

                    results.Add(new ProductDto
                    {
                        ExternalId = externalId,
                        Title = title,
                        Brand = brand,
                        Barcode = barcode,
                        ImageUrl = imageUrl,
                        Price = price,
                        RegularPrice = regularPrice,
                        ProductUrl = productUrl,
                        CategoryName = categoryName,
                        ParentCategoryName = parentCategoryName,
                        RootCategoryName = rootCategoryName
                    });
                }
            }

            return results;
        }

        // Her urun karti kendi gercek site kategorisini "categories" dizisinde tasir, kok
        // kategoriden en spesifik (yaprak) kategoriye dogru siralanmis halde — orn. ["Süt &
        // Kahvaltılık", "Süt"] ya da tek seviyeli "Dondurma" gibi. Bu, CategoryResolver'in
        // baslik bazli anahtar kelime tahminine (CategoryClassifier) hic basvurmadan, dogrudan
        // sitenin kendi kategorizasyonunu kullanmasini saglar.
        private static (string? CategoryName, string? ParentCategoryName, string? RootCategoryName) ParseCategory(JsonElement p)
        {
            if (!p.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
                return (null, null, null);

            var names = categories.EnumerateArray()
                .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            return names.Count switch
            {
                0 => (null, null, null),
                1 => (names[0], null, null),
                2 => (names[1], null, names[0]),
                _ => (names[^1], names[^2], names[0])
            };
        }

        // JSON agacini dolasarak 'product-list' bilesenini ve onun altindaki 'products' dizisini recursive olarak bulur.
        private static JsonElement? FindProductsArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("component", out var comp) && comp.GetString() == "product-list")
                {
                    if (element.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
                    {
                        return products;
                    }
                }

                foreach (var prop in element.EnumerateObject())
                {
                    var found = FindProductsArray(prop.Value);
                    if (found != null) return found;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindProductsArray(item);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}