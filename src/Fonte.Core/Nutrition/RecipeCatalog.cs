using System.Text.Json;
using Fonte.Core.Models;

namespace Fonte.Core.Nutrition;

/// <param name="Code">The CIQUAL code of the food.</param>
/// <param name="Grams">How much goes into the whole recipe.</param>
public sealed record RecipeIngredientLine(string Code, double Grams);

/// <summary>A built-in recipe: its foods in grams for all its portions, and its steps in each language.</summary>
public sealed record CatalogRecipe(
    string Key, Meal Meal, int Servings, int Minutes, IReadOnlyList<string> Tags, IReadOnlyList<RecipeIngredientLine> Ingredients,
    IReadOnlyDictionary<string, string> Names, IReadOnlyDictionary<string, IReadOnlyList<string>> Steps)
{
    public string Name(string language) => Names.TryGetValue(language, out var n) ? n : Names["en"];

    public IReadOnlyList<string> StepsIn(string language) => Steps.TryGetValue(language, out var s) ? s : Steps["en"];

    /// <summary>Grams of one portion.</summary>
    public double PortionGrams => Ingredients.Sum(i => i.Grams) / Servings;

    /// <summary>Calories and macronutrients of one portion, from the CIQUAL table.</summary>
    public Macros PortionMacros
    {
        get
        {
            double k = 0, p = 0, c = 0, f = 0;
            foreach (var line in Ingredients)
            {
                if (RecipeCatalog.Food(line.Code) is not { } food)
                    continue;
                k += food.Kcal * line.Grams / 100;
                p += food.Protein * line.Grams / 100;
                c += food.Carbs * line.Grams / 100;
                f += food.Fat * line.Grams / 100;
            }
            return new Macros(k / Servings, p / Servings, c / Servings, f / Servings);
        }
    }
}

/// <summary>A recipe offered for a meal, with the portions that fit what is left to eat.</summary>
public sealed record RecipeSuggestion(CatalogRecipe Recipe, double Portions, Macros Macros);

/// <summary>The recipes shipped with the app (Fonte Pro), and the ones that fit a meal.</summary>
public static class RecipeCatalog
{
    private static readonly Lazy<IReadOnlyList<CatalogRecipe>> Recipes = new(Load);
    private static readonly Lazy<IReadOnlyDictionary<string, CatalogFood>> Foods = new(() => FoodCatalog.All.ToDictionary(f => f.Code));

    public static IReadOnlyList<CatalogRecipe> All => Recipes.Value;

    public static CatalogRecipe? Find(string key) => All.FirstOrDefault(r => r.Key == key);

    internal static CatalogFood? Food(string code) => Foods.Value.GetValueOrDefault(code);

    /// <summary>Share of the day's calories usually eaten at each meal.</summary>
    public static double MealShare(Meal meal) => meal switch
    {
        Meal.Breakfast => 0.25,
        Meal.Lunch => 0.35,
        Meal.Dinner => 0.30,
        _ => 0.10,
    };

    /// <summary>
    /// The recipes of a meal, best fit first, each scaled (by quarter portions, 0.5 to 3) so its calories reach
    /// <paramref name="target"/>; among those, the closer the protein to the target, the better.
    /// </summary>
    public static IReadOnlyList<RecipeSuggestion> Suggest(Macros target, Meal? meal, IEnumerable<CatalogRecipe>? recipes = null) =>
        (recipes ?? All)
            .Where(r => meal is null || r.Meal == meal || (meal is Meal.Lunch or Meal.Dinner && r.Meal is Meal.Lunch or Meal.Dinner))
            .Select(r => Scale(r, target))
            .OrderBy(s => Miss(s.Macros, target))
            .ToList();

    public static RecipeSuggestion Scale(CatalogRecipe recipe, Macros target)
    {
        var portion = recipe.PortionMacros;
        var portions = target.Kcal > 0 && portion.Kcal > 0
            ? Math.Clamp(Math.Round(target.Kcal / portion.Kcal * 4) / 4, 0.5, 3)
            : 1;
        return new RecipeSuggestion(recipe, portions, Times(portion, portions));
    }

    public static Macros Times(Macros m, double factor) => new(m.Kcal * factor, m.Protein * factor, m.Carbs * factor, m.Fat * factor);

    /// <summary>How far a meal is from its target: calories and, twice as much, protein (relative errors).</summary>
    private static double Miss(Macros m, Macros target) =>
        (target.Kcal > 0 ? Math.Abs(m.Kcal - target.Kcal) / target.Kcal : 0)
        + 2 * (target.Protein > 0 ? Math.Max(0, target.Protein - m.Protein) / target.Protein : 0);

    private sealed record RecipeJson(
        string Key, string Meal, int Servings, int Minutes, List<string> Tags, List<IngredientJson> Ingredients,
        Dictionary<string, string> Name, Dictionary<string, List<string>> Steps);

    private sealed record IngredientJson(string Code, double Grams);

    private static IReadOnlyList<CatalogRecipe> Load()
    {
        using var stream = typeof(RecipeCatalog).Assembly.GetManifestResourceStream("Fonte.Core.recipes.json")
            ?? throw new InvalidOperationException("The recipes are missing from the assembly.");
        var items = JsonSerializer.Deserialize<List<RecipeJson>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return items.Select(r => new CatalogRecipe(
            r.Key, Enum.Parse<Meal>(r.Meal), r.Servings, r.Minutes, r.Tags,
            r.Ingredients.Select(i => new RecipeIngredientLine(i.Code, i.Grams)).ToList(),
            r.Name, r.Steps.ToDictionary(s => s.Key, s => (IReadOnlyList<string>)s.Value))).ToList();
    }
}
