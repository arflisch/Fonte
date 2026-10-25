using Fonte.Core.Models;
using Fonte.Core.Nutrition;

namespace Fonte.Core.Tests;

public sealed class NutritionTests : StoreTestBase
{
    private static readonly DateTime Today = new(2026, 10, 25, 12, 0, 0);

    [Fact]
    public void The_ciqual_table_is_shipped_with_its_nutrients()
    {
        Assert.True(FoodCatalog.All.Count > 3000);
        var apple = FoodCatalog.All.Single(f => f.NameFr == "Pomme, pulpe et peau, crue");
        Assert.Equal(("Apple, pulp and peel, raw", 53.8, 11.6), (apple.NameEn, apple.Kcal, apple.Carbs));
        Assert.All(FoodCatalog.All, f => Assert.InRange(f.Kcal, 0, 950));
        Assert.Equal(FoodCatalog.All.Count, FoodCatalog.All.Select(f => f.Code).Distinct().Count());
    }

    [Theory]
    [InlineData(Sex.Male, 30, 180, 80, 1780)]
    [InlineData(Sex.Female, 30, 165, 60, 1320)] // 600 + 1031.25 − 150 − 161
    public void Resting_metabolism_follows_mifflin_st_jeor(Sex sex, int age, double height, double weight, double expected)
    {
        Assert.Equal(expected, Math.Round(Energy.Bmr(sex, age, height, weight)));
    }

    [Fact]
    public void Losing_half_a_kilo_a_week_takes_550_kcal_a_day_off_maintenance()
    {
        var profile = new NutritionProfile(Sex.Male, 1996, 180, ActivityLevel.Moderate, WeightGoal.Lose, 0.5, 2);

        var targets = Energy.Targets(profile, 80, Today);

        Assert.Equal((1780d, 2759d), (targets.Bmr, targets.Maintenance)); // 1780 × 1.55
        Assert.Equal(2210, targets.Kcal);                                  // 2759 − 550, to the nearest 10
        Assert.Equal((160d, 61d), (targets.Protein, targets.Fat));        // 2 g/kg, 25 % of the calories
        Assert.Equal(Math.Round((2210 - 160 * 4 - 61 * 9) / 4.0), targets.Carbs);
        Assert.False(targets.IsFloored);
    }

    [Fact]
    public void A_target_never_goes_under_the_safe_minimum()
    {
        var profile = new NutritionProfile(Sex.Female, 1990, 155, ActivityLevel.Sedentary, WeightGoal.Lose, 1, 1.8);

        var targets = Energy.Targets(profile, 50, Today);

        Assert.Equal(1200, targets.Kcal);
        Assert.True(targets.IsFloored);
    }

    [Fact]
    public void The_goal_date_follows_the_weekly_pace()
    {
        Assert.Equal(Today.Date.AddDays(56), Energy.GoalDate(80, 76, 0.5, Today));
        Assert.Null(Energy.GoalDate(80, 76, 0, Today));
        Assert.Equal(Today.Date, Energy.GoalDate(76, 76, 0.5, Today));
    }

    [Fact]
    public async Task Foods_are_found_without_accents_and_raw_foods_first_when_the_name_starts_with_the_word()
    {
        var found = await Store.SearchFoodsAsync("pomme crue");

        Assert.NotEmpty(found);
        Assert.All(found, f => Assert.Contains("pomme", f.SearchText));
        Assert.Contains(found, f => f.Name == "Pomme, pulpe et peau, crue");
        Assert.Contains((await Store.SearchFoodsAsync("oeuf")), f => f.Name.StartsWith("Oeuf", StringComparison.Ordinal));
        Assert.Contains((await Store.SearchFoodsAsync("chicken breast")), f => f.NameEn!.StartsWith("Chicken, breast", StringComparison.Ordinal));
        Assert.Contains((await Store.SearchFoodsAsync("flocons avoine")), f => f.Name == "Flocon d'avoine");
    }

    [Fact]
    public async Task A_day_adds_up_its_meals_and_water()
    {
        var rice = (await Store.SearchFoodsAsync("riz blanc cuit")).First();
        var chicken = (await Store.SearchFoodsAsync("poulet filet sans peau cru")).First();
        await Store.AddFoodEntryAsync(Today, Meal.Lunch, rice.Id, 200, Today);
        await Store.AddFoodEntryAsync(Today, Meal.Lunch, chicken.Id, 150, Today);
        await Store.AddFoodEntryAsync(Today.AddDays(-1), Meal.Dinner, rice.Id, 100, Today);
        await Store.AddWaterAsync(Today, 250, Today);
        await Store.AddWaterAsync(Today, 500, Today);
        await Store.RemoveLastWaterAsync(Today);

        var day = await Store.GetNutritionDayAsync(Today);

        var lunch = day.Meals.Single(m => m.Meal == Meal.Lunch);
        Assert.Equal(2, lunch.Foods.Count);
        Assert.Equal(Math.Round(Energy.Scale(rice.Kcal, 200) + Energy.Scale(chicken.Kcal, 150)), day.Totals.Kcal);
        Assert.Equal(250, day.WaterMl);
        Assert.Empty(day.Meals.Single(m => m.Meal == Meal.Dinner).Foods);
        Assert.Equal([rice.Id, chicken.Id], (await Store.GetRecentFoodsAsync()).Select(f => f.Id).Order());
    }

    [Fact]
    public async Task Changing_a_quantity_recomputes_the_nutrients()
    {
        var oats = (await Store.SearchFoodsAsync("flocons avoine")).First();
        var entry = await Store.AddFoodEntryAsync(Today, Meal.Breakfast, oats.Id, 50, Today);

        var updated = await Store.UpdateFoodEntryAsync(entry.Id, 80, Meal.Snack, Today);

        Assert.Equal((80d, Meal.Snack, Energy.Scale(oats.Kcal, 80)), (updated.Grams, updated.Meal, updated.Kcal));
        await Assert.ThrowsAsync<FonteException>(() => Store.UpdateFoodEntryAsync(entry.Id, 0, Meal.Snack, Today));
    }

    [Fact]
    public async Task A_meal_can_be_copied_to_another_day_and_cleared()
    {
        var banana = (await Store.SearchFoodsAsync("banane pulpe crue")).First();
        await Store.AddFoodEntryAsync(Today.AddDays(-1), Meal.Breakfast, banana.Id, 120, Today);

        var copies = await Store.CopyMealAsync(Today.AddDays(-1), Today, Meal.Breakfast, Today);
        Assert.Single(copies);
        Assert.Single((await Store.GetNutritionDayAsync(Today)).Meals[0].Foods);

        var cleared = await Store.ClearMealAsync(Today, Meal.Breakfast);
        Assert.Single(cleared);
        Assert.Empty((await Store.GetNutritionDayAsync(Today)).Meals[0].Foods);
    }

    [Fact]
    public async Task A_dish_gets_its_nutrients_from_its_ingredients_and_its_cooked_weight()
    {
        var pasta = await Store.SaveCustomFoodAsync(new Food { Name = "Pâtes", Kcal = 350, Protein = 12, Carbs = 70, Fat = 1.5 });
        var sauce = await Store.SaveCustomFoodAsync(new Food { Name = "Sauce", Kcal = 100, Protein = 2, Carbs = 10, Fat = 5 });

        // 200 g of dry pasta and 300 g of sauce make 800 g once cooked, in 4 portions.
        var dish = await Store.SaveRecipeAsync(null, "Pâtes bolo", [(pasta.Id, 200), (sauce.Id, 300)], 800, 4, Today);

        Assert.Equal(FoodSource.Recipe, dish.Source);
        Assert.Equal((800d, 200d), (dish.TotalGrams!.Value, dish.ServingGrams!.Value));
        Assert.Equal(Math.Round((700 + 300) / 8.0, 2), dish.Kcal); // 1000 kcal in 800 g
        Assert.Equal(2, (await Store.GetRecipeAsync(dish.Id)).Count);
        var error = await Assert.ThrowsAsync<FonteException>(() => Store.SaveRecipeAsync(null, "Vide", [], null, 1, Today));
        Assert.Equal(FonteError.EmptyRecipe, error.Error);
    }

    [Fact]
    public async Task Impossible_nutrients_are_refused()
    {
        var error = await Assert.ThrowsAsync<FonteException>(() =>
            Store.SaveCustomFoodAsync(new Food { Name = "Erreur", Kcal = 400, Protein = 60, Carbs = 60, Fat = 10 }));
        Assert.Equal(FonteError.InvalidFood, error.Error);
    }

    [Fact]
    public async Task A_used_food_is_hidden_rather_than_deleted()
    {
        var bar = await Store.SaveCustomFoodAsync(new Food { Name = "Barre protéinée", Kcal = 380, Protein = 30, Carbs = 35, Fat = 12, ServingGrams = 60 });
        var unused = await Store.SaveCustomFoodAsync(new Food { Name = "Jamais mangé", Kcal = 100 });
        await Store.AddFoodEntryAsync(Today, Meal.Snack, bar.Id, 60, Today);

        await Store.RemoveFoodAsync(bar.Id);
        await Store.RemoveFoodAsync(unused.Id);

        Assert.True((await Store.GetFoodAsync(bar.Id))!.IsArchived);
        Assert.Null(await Store.GetFoodAsync(unused.Id));
        Assert.Empty(await Store.SearchFoodsAsync("barre proteinee"));
        Assert.Single((await Store.GetNutritionDayAsync(Today)).Meals[3].Foods);
    }

    [Fact]
    public async Task A_scanned_product_is_kept_once_per_barcode()
    {
        var product = new Product("3017620422003", "Nutella", "Ferrero", 539, 6.3, 57.5, 30.9, 56.3, null, 0.107, 15, "15 g");

        var first = await Store.SaveProductAsync(product);
        var again = await Store.SaveProductAsync(product with { Kcal = 540 });

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(540, (await Store.FindProductAsync("3017620422003"))!.Kcal);
        Assert.Contains((await Store.SearchFoodsAsync("ferrero")), f => f.Id == first.Id);
    }

    [Fact]
    public async Task Daily_intake_lists_every_day_of_the_range()
    {
        var rice = (await Store.SearchFoodsAsync("riz blanc cuit")).First();
        await Store.AddFoodEntryAsync(Today.AddDays(-2), Meal.Lunch, rice.Id, 100, Today);

        var week = await Store.GetDailyIntakeAsync(Today.AddDays(-6), Today);

        Assert.Equal(7, week.Count);
        Assert.Equal(Math.Round(rice.Kcal), week[4].Kcal);
        Assert.Equal(0, week[6].Kcal);
    }

    [Fact]
    public void Open_food_facts_products_are_read_per_100_g()
    {
        const string json = """
            {"code":"3017620422003","status":1,"product":{"code":"3017620422003","product_name":"Nutella","product_name_fr":"Nutella pâte à tartiner",
             "brands":"Ferrero, Nutella","serving_quantity":"15","serving_size":"15 g",
             "nutriments":{"energy-kcal_100g":539,"proteins_100g":6.3,"carbohydrates_100g":"57,5","fat_100g":30.9,"sugars_100g":56.3,"salt_100g":0.107}}}
            """;

        var product = OpenFoodFacts.ParseProduct(json, "fr")!;

        Assert.Equal(("Nutella pâte à tartiner", "Ferrero", 539d, 57.5, 15d), (product.Name, product.Brand, product.Kcal, product.Carbs, product.ServingGrams!.Value));
        Assert.Null(OpenFoodFacts.ParseProduct("""{"status":0,"status_verbose":"product not found"}""", "fr"));
        Assert.Null(OpenFoodFacts.ParseProduct("""{"status":1,"product":{"code":"1","product_name":"Sans énergie","nutriments":{}}}""", "fr"));
    }

    [Fact]
    public void Energy_in_kilojoules_is_converted()
    {
        const string json = """{"products":[{"code":"1","product_name":"Jus","nutriments":{"energy_100g":184.1,"carbohydrates_100g":10}},{"code":"2","nutriments":{}}]}""";

        var products = OpenFoodFacts.ParseSearch(json, "en");

        Assert.Equal(44, Assert.Single(products).Kcal);
    }
}
