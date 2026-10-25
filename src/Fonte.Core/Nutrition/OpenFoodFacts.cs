using System.Globalization;
using System.Text.Json;

namespace Fonte.Core.Nutrition;

/// <summary>A packaged product as Open Food Facts describes it, with its nutrients per 100 g.</summary>
public sealed record Product(
    string Barcode, string Name, string? Brand, double Kcal, double Protein, double Carbs, double Fat,
    double? Sugars, double? Fiber, double? Salt, double? ServingGrams, string? ServingName);

/// <summary>
/// Reads the answers of the Open Food Facts API (https://world.openfoodfacts.org, data under the Open Database
/// License). Only products with their energy per 100 g are kept: without it, they can't be counted.
/// </summary>
public static class OpenFoodFacts
{
    /// <summary>Fields asked for, to keep answers small.</summary>
    public const string Fields = "code,product_name,product_name_fr,product_name_en,brands,nutriments,serving_quantity,serving_size";

    public static string ProductUrl(string barcode) =>
        $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(barcode)}.json?fields={Fields}";

    public static string SearchUrl(string query) =>
        $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&search_simple=1&action=process&json=1&page_size=25&fields={Fields}";

    /// <summary>The product of a barcode lookup; null when it is unknown or has no energy.</summary>
    public static Product? ParseProduct(string json, string language)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() != 1)
            return null;
        return root.TryGetProperty("product", out var product) ? Read(product, language) : null;
    }

    /// <summary>The products of a text search that can be counted.</summary>
    public static IReadOnlyList<Product> ParseSearch(string json, string language)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return [];
        return products.EnumerateArray().Select(p => Read(p, language)).OfType<Product>().ToList();
    }

    private static Product? Read(JsonElement p, string language)
    {
        var name = Text(p, $"product_name_{language}") ?? Text(p, "product_name") ?? Text(p, "product_name_fr") ?? Text(p, "product_name_en");
        var code = Text(p, "code");
        if (name is null || code is null || !p.TryGetProperty("nutriments", out var n))
            return null;
        var kcal = Number(n, "energy-kcal_100g") ?? Number(n, "energy_100g") / 4.184;
        if (kcal is not { } energy || energy < 0 || energy > 950)
            return null;
        var brand = Text(p, "brands")?.Split(',')[0].Trim();
        var serving = Number(p, "serving_quantity");
        return new Product(
            code, name, string.IsNullOrEmpty(brand) ? null : brand, Math.Round(energy, 1),
            Number(n, "proteins_100g") ?? 0, Number(n, "carbohydrates_100g") ?? 0, Number(n, "fat_100g") ?? 0,
            Number(n, "sugars_100g"), Number(n, "fiber_100g"), Number(n, "salt_100g"),
            serving is > 0 and < 5000 ? serving : null, Text(p, "serving_size"));
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()?.Trim() is { Length: > 0 } s ? s : null;

    /// <summary>Open Food Facts writes numbers sometimes as numbers, sometimes as strings.</summary>
    private static double? Number(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return double.IsFinite(d) ? d : null;
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return double.IsFinite(s) ? s : null;
        return null;
    }
}
