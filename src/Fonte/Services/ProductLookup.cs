using System.Net.Http.Headers;
using Fonte.Core.Nutrition;
using Fonte.Localization;

namespace Fonte.Services;

public enum LookupStatus
{
    Found,
    NotFound,

    /// <summary>No network, or Open Food Facts didn't answer.</summary>
    Offline,
}

/// <summary>
/// Looks products up on Open Food Facts, the only time Fonte goes online: the barcode or the words typed are sent,
/// nothing else. Data: Open Food Facts contributors, Open Database License.
/// </summary>
public sealed class ProductLookup
{
    private static readonly HttpClient Http = CreateClient();

    public async Task<(LookupStatus Status, Product? Product)> FindAsync(string barcode, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await Http.GetAsync(OpenFoodFacts.ProductUrl(barcode), cancellation);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (LookupStatus.NotFound, null);
            if (!response.IsSuccessStatusCode)
                return (LookupStatus.Offline, null);
            var product = OpenFoodFacts.ParseProduct(await response.Content.ReadAsStringAsync(cancellation), Localizer.Instance.Language.Code);
            return product is null ? (LookupStatus.NotFound, null) : (LookupStatus.Found, product);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (LookupStatus.Offline, null);
        }
    }

    public async Task<(LookupStatus Status, IReadOnlyList<Product> Products)> SearchAsync(string query, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await Http.GetAsync(OpenFoodFacts.SearchUrl(query), cancellation);
            if (!response.IsSuccessStatusCode)
                return (LookupStatus.Offline, []);
            var products = OpenFoodFacts.ParseSearch(await response.Content.ReadAsStringAsync(cancellation), Localizer.Instance.Language.Code);
            return (products.Count > 0 ? LookupStatus.Found : LookupStatus.NotFound, products);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (LookupStatus.Offline, []);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // Open Food Facts asks apps to name themselves.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Fonte", AppInfo.Current.VersionString));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/arflisch/Fonte)"));
        return client;
    }
}
