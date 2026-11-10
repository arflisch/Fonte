using Fonte.Core.Models;
using Fonte.Core.Nutrition;

namespace Fonte.Core.Tests;

public sealed class RecipeTests : StoreTestBase
{
    private static readonly DateTime Today = new(2026, 11, 10, 12, 0, 0);

    [Fact]
    public void Every_recipe_is_made_of_known_foods_and_written_in_three_languages()
    {
        Assert.True(RecipeCatalog.All.Count >= 30);
        Assert.Equal(RecipeCatalog.All.Count, RecipeCatalog.All.Select(r => r.Key).Distinct().Count());
        Assert.All(RecipeCatalog.All, r =>
        {
            Assert.All(r.Ingredients, i => Assert.NotNull(FoodCatalog.All.FirstOrDefault(f => f.Code == i.Code)));
            Assert.All(new[] { "fr", "en", "nl" }, lang =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Name(lang)));
                Assert.NotEmpty(r.StepsIn(lang));
            });
            Assert.Equal(r.StepsIn("fr").Count, r.StepsIn("nl").Count);
            Assert.InRange(r.PortionMacros.Kcal, 150, 800);
        });
        Assert.All(Enum.GetValues<Meal>(), m => Assert.Contains(RecipeCatalog.All, r => r.Meal == m));
    }

    [Fact]
    public void Suggestions_are_scaled_to_the_calories_left_and_favour_protein()
    {
        var target = new Macros(700, 50, 70, 20);
        var suggestions = RecipeCatalog.Suggest(target, Meal.Lunch);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.True(s.Recipe.Meal is Meal.Lunch or Meal.Dinner));
        var best = suggestions[0];
        Assert.InRange(best.Macros.Kcal, 600, 800);
        Assert.True(best.Macros.Protein >= 40);
        Assert.Equal(0, best.Portions * 4 % 1);
    }

    [Fact]
    public void Chosen_foods_get_the_grams_that_reach_the_meal_target()
    {
        PlanFood Food(int id, double k, double p, double c, double f)
        {
            var (min, max) = PortionSolver.Bounds(k, p, f);
            return new PlanFood(id, id.ToString(), k, p, c, f, min, max);
        }
        var chicken = Food(1, 110, 23.4, 0, 1.5);
        var rice = Food(2, 117, 2.7, 24.4, 0.6);
        var broccoli = Food(3, 36, 2.8, 4, 0.4);
        var oil = Food(4, 900, 0, 0, 100);

        var plan = PortionSolver.Solve([chicken, rice, broccoli, oil], new Macros(650, 45, 70, 18));

        Assert.InRange(plan.Totals.Kcal, 585, 715);
        Assert.InRange(plan.Totals.Protein, 40, 52);
        Assert.All(plan.Portions, p => Assert.InRange(p.Grams, p.Food.MinGrams, p.Food.MaxGrams));
        Assert.All(plan.Portions, p => Assert.Equal(0, p.Grams % 5));
        Assert.Equal(PortionSolver.Role.Fat, PortionSolver.RoleOf(900, 0, 0, 100));
        Assert.Equal(PortionSolver.Role.Protein, PortionSolver.RoleOf(110, 23, 0, 1.5));
        Assert.Equal(PortionSolver.Role.Starch, PortionSolver.RoleOf(117, 2.7, 24, 0.6));
        Assert.Equal(PortionSolver.Role.Vegetable, PortionSolver.RoleOf(36, 2.8, 4, 0.4));
    }

    [Fact]
    public async Task A_logged_recipe_becomes_a_dish_reused_the_next_time()
    {
        var recipe = RecipeCatalog.Find("chicken_rice_broccoli")!;

        var (entry, dish) = await Store.LogCatalogRecipeAsync(recipe, recipe.Name("en"), 1.5, Today, Meal.Lunch, Today);
        var (again, same) = await Store.LogCatalogRecipeAsync(recipe, recipe.Name("en"), 1, Today.AddDays(1), Meal.Dinner, Today);

        Assert.Equal(dish.Id, same.Id);
        Assert.Equal(FoodSource.Recipe, dish.Source);
        Assert.Equal(recipe.PortionMacros.Kcal * 1.5, entry.Kcal, 0);
        Assert.Equal(recipe.PortionMacros.Kcal, again.Kcal, 0);
        Assert.Equal(recipe.PortionMacros.Protein * 1.5, entry.Protein, 0);
    }
}
