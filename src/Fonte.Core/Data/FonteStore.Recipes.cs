using Fonte.Core.Models;
using Fonte.Core.Nutrition;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>The foods of a built-in recipe, as rows of the table (the CIQUAL foods it is made of).</summary>
    public async Task<IReadOnlyList<(Food Food, double Grams)>> GetCatalogRecipeFoodsAsync(CatalogRecipe recipe)
    {
        var db = await GetFoodsConnectionAsync();
        var parts = new List<(Food, double)>();
        foreach (var line in recipe.Ingredients)
        {
            var food = await db.Table<Food>().Where(f => f.Source == FoodSource.Catalog && f.Code == line.Code).FirstOrDefaultAsync();
            if (food is not null)
                parts.Add((food, line.Grams));
        }
        return parts;
    }

    /// <summary>
    /// Logs portions of a built-in recipe at a meal. The recipe becomes one of the user's dishes (made once, then
    /// reused), so it shows as one line in the journal and comes back in the search.
    /// </summary>
    public async Task<(FoodEntry Entry, Food Dish)> LogCatalogRecipeAsync(CatalogRecipe recipe, string name, double portions, DateTime date, Meal meal, DateTime now)
    {
        var db = await GetFoodsConnectionAsync();
        var code = "recipe:" + recipe.Key;
        var dish = await db.Table<Food>().Where(f => f.Source == FoodSource.Recipe && f.Code == code).FirstOrDefaultAsync();
        if (dish is null)
        {
            var parts = await GetCatalogRecipeFoodsAsync(recipe);
            dish = await SaveRecipeAsync(null, name, parts.Select(p => (p.Food.Id, p.Grams)).ToList(), null, recipe.Servings, now);
            dish.Code = code;
            dish.IsArchived = false;
            await db.UpdateAsync(dish);
        }
        else if (dish.IsArchived)
        {
            dish.IsArchived = false;
            await db.UpdateAsync(dish);
        }
        var grams = Math.Round((dish.ServingGrams ?? recipe.PortionGrams) * portions, 1);
        var entry = await AddFoodEntryAsync(date, meal, dish.Id, grams, now);
        return (entry, dish);
    }

    /// <summary>Logs every food of a meal worked out from chosen ingredients.</summary>
    public async Task<IReadOnlyList<FoodEntry>> LogMealPlanAsync(MealPlan plan, DateTime date, Meal meal, DateTime now)
    {
        var entries = new List<FoodEntry>();
        foreach (var (food, grams) in plan.Portions.Where(p => p.Grams > 0))
            entries.Add(await AddFoodEntryAsync(date, meal, food.FoodId, grams, now));
        return entries;
    }
}
