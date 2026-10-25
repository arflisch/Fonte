using System.Globalization;

namespace Fonte.Core.Nutrition;

/// <param name="Group">CIQUAL food group code (1 dishes, 2 fruit and vegetables, 3 cereals, 4 meat and fish…).</param>
public sealed record CatalogFood(
    string Code, int Group, string NameFr, string NameEn,
    double Kcal, double Protein, double Carbs, double Fat, double? Sugars, double? Fiber, double? Salt);

/// <summary>
/// The ANSES CIQUAL 2020 food composition table, shipped with the app: about 3,000 raw and generic foods with their
/// nutrients per 100 g, in French and English. Source: ANSES, Ciqual 2020 (Licence Ouverte).
/// </summary>
public static class FoodCatalog
{
    private static readonly Lazy<IReadOnlyList<CatalogFood>> Foods = new(Load);

    public static IReadOnlyList<CatalogFood> All => Foods.Value;

    private static IReadOnlyList<CatalogFood> Load()
    {
        using var stream = typeof(FoodCatalog).Assembly.GetManifestResourceStream("Fonte.Core.ciqual.csv")
            ?? throw new InvalidOperationException("The CIQUAL table is missing from the assembly.");
        using var reader = new StreamReader(stream);
        reader.ReadLine(); // header
        var foods = new List<CatalogFood>();
        while (reader.ReadLine() is { } line)
        {
            var c = line.Split(';');
            if (c.Length < 11)
                continue;
            foods.Add(new CatalogFood(
                c[0], int.Parse(c[1], CultureInfo.InvariantCulture), c[2], c[3],
                Number(c[4]) ?? 0, Number(c[5]) ?? 0, Number(c[6]) ?? 0, Number(c[7]) ?? 0,
                Number(c[8]), Number(c[9]), Number(c[10])));
        }
        return foods;
    }

    private static double? Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
