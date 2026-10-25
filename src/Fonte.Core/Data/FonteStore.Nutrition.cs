using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using SQLite;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>Most results a food search returns.</summary>
    public const int SearchLimit = 60;

    private const double MaxGrams = 5000;

    private readonly SemaphoreSlim _catalogLock = new(1, 1);
    private bool _catalogReady;

    /// <summary>Foods matching every word of <paramref name="query"/>: favourites, recent and own foods first.</summary>
    public async Task<IReadOnlyList<Food>> SearchFoodsAsync(string query, int limit = SearchLimit)
    {
        var db = await GetFoodsConnectionAsync();
        var words = TextSearch.Words(query).Select(TextSearch.Singular).ToArray();
        if (words.Length == 0)
            return [];
        var where = string.Join(" AND ", words.Select(_ => "SearchText LIKE ?"));
        var args = words.Select(w => (object)$"%{w}%").ToArray();
        var found = await db.QueryAsync<Food>($"SELECT * FROM foods WHERE IsArchived = 0 AND {where}", args);
        var first = words[0];
        return found
            .OrderByDescending(f => f.IsFavorite)
            .ThenByDescending(f => f.LastUsedAt ?? DateTime.MinValue)
            .ThenBy(f => StartsWith(f, first) ? 0 : 1)
            .ThenBy(f => f.Source == FoodSource.Catalog ? 1 : 0)
            .ThenBy(f => f.Name.Length)
            .Take(limit)
            .ToList();
    }

    /// <summary>Foods eaten lately, most recent first.</summary>
    public async Task<IReadOnlyList<Food>> GetRecentFoodsAsync(int limit = 30)
    {
        var db = await GetFoodsConnectionAsync();
        return await db.Table<Food>().Where(f => f.LastUsedAt != null && !f.IsArchived)
            .OrderByDescending(f => f.LastUsedAt).Take(limit).ToListAsync();
    }

    public async Task<IReadOnlyList<Food>> GetFavoriteFoodsAsync()
    {
        var db = await GetFoodsConnectionAsync();
        return (await db.Table<Food>().Where(f => f.IsFavorite && !f.IsArchived).ToListAsync()).OrderBy(f => f.Name).ToList();
    }

    /// <summary>The user's own foods, dishes and scanned products.</summary>
    public async Task<IReadOnlyList<Food>> GetOwnFoodsAsync()
    {
        var db = await GetFoodsConnectionAsync();
        return (await db.Table<Food>().Where(f => f.Source != FoodSource.Catalog && !f.IsArchived).ToListAsync())
            .OrderByDescending(f => f.LastUsedAt ?? f.CreatedAt).ToList();
    }

    public async Task<Food?> GetFoodAsync(int foodId)
    {
        var db = await GetFoodsConnectionAsync();
        return await db.FindAsync<Food>(foodId);
    }

    /// <summary>A product already scanned, by its barcode.</summary>
    public async Task<Food?> FindProductAsync(string barcode)
    {
        var db = await GetFoodsConnectionAsync();
        return await db.Table<Food>().Where(f => f.Source == FoodSource.Product && f.Code == barcode).FirstOrDefaultAsync();
    }

    /// <summary>Keeps a product found on Open Food Facts, updating it when it was already known.</summary>
    public async Task<Food> SaveProductAsync(Product product)
    {
        var db = await GetFoodsConnectionAsync();
        var food = await FindProductAsync(product.Barcode) ?? new Food { Source = FoodSource.Product, Code = product.Barcode };
        food.Name = Trim(product.Name, 160);
        food.Brand = product.Brand is null ? null : Trim(product.Brand, 80);
        food.Kcal = product.Kcal;
        food.Protein = product.Protein;
        food.Carbs = product.Carbs;
        food.Fat = product.Fat;
        food.Sugars = product.Sugars;
        food.Fiber = product.Fiber;
        food.Salt = product.Salt;
        food.ServingGrams = product.ServingGrams;
        food.ServingName = product.ServingName is null ? null : Trim(product.ServingName, 40);
        food.IsArchived = false;
        Validate(food);
        await SaveFoodAsync(db, food);
        return food;
    }

    /// <summary>Creates or updates one of the user's foods (nutrients per 100 g).</summary>
    public async Task<Food> SaveCustomFoodAsync(Food food)
    {
        if (food.Source is FoodSource.Catalog or FoodSource.Recipe)
            throw new ArgumentException("Only the user's own foods are saved here.", nameof(food));
        food.Name = food.Name.Trim();
        food.Brand = string.IsNullOrWhiteSpace(food.Brand) ? null : food.Brand.Trim();
        if (food.Name.Length == 0)
            throw new FonteException(FonteError.EmptyName, "A food needs a name.");
        food.Source = food.Code is null ? FoodSource.Custom : food.Source;
        Validate(food);
        var db = await GetFoodsConnectionAsync();
        await SaveFoodAsync(db, food);
        return food;
    }

    public async Task SetFavoriteAsync(int foodId, bool favorite)
    {
        var db = await GetFoodsConnectionAsync();
        var food = await FindFoodAsync(db, foodId);
        food.IsFavorite = favorite;
        await db.UpdateAsync(food);
        OnChanged();
    }

    /// <summary>Removes an own food or dish; one already eaten is only hidden, so past meals still show it.</summary>
    public async Task RemoveFoodAsync(int foodId)
    {
        var db = await GetFoodsConnectionAsync();
        var food = await FindFoodAsync(db, foodId);
        if (food.Source == FoodSource.Catalog)
            throw new ArgumentException("Catalog foods can't be removed.", nameof(foodId));
        var used = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM food_entries WHERE FoodId = ?", foodId) > 0
            || await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recipe_ingredients WHERE FoodId = ?", foodId) > 0;
        if (used)
        {
            food.IsArchived = true;
            food.IsFavorite = false;
            await db.UpdateAsync(food);
        }
        else
        {
            await db.RunInTransactionAsync(conn =>
            {
                conn.Execute("DELETE FROM recipe_ingredients WHERE RecipeId = ?", foodId);
                conn.Delete(food);
            });
        }
        OnChanged();
    }

    /// <summary>Logs <paramref name="grams"/> of a food at a meal of a day.</summary>
    public async Task<FoodEntry> AddFoodEntryAsync(DateTime date, Meal meal, int foodId, double grams, DateTime now)
    {
        CheckGrams(grams);
        var db = await GetFoodsConnectionAsync();
        var food = await FindFoodAsync(db, foodId);
        var entry = new FoodEntry { Date = date.Date, Meal = meal, FoodId = foodId, CreatedAt = now, UpdatedAt = now };
        Fill(entry, food, grams);
        food.LastUsedAt = now;
        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(entry);
            conn.Update(food);
        });
        OnChanged();
        return entry;
    }

    /// <summary>Changes the quantity or the meal of a logged food; nutrients follow the food's current values.</summary>
    public async Task<FoodEntry> UpdateFoodEntryAsync(int entryId, double grams, Meal meal, DateTime now)
    {
        CheckGrams(grams);
        var db = await GetFoodsConnectionAsync();
        var entry = await FindEntryAsync(db, entryId);
        var food = await FindFoodAsync(db, entry.FoodId);
        entry.Meal = meal;
        entry.UpdatedAt = now;
        Fill(entry, food, grams);
        await db.UpdateAsync(entry);
        OnChanged();
        return entry;
    }

    public async Task<FoodEntry?> GetFoodEntryAsync(int entryId)
    {
        var db = await GetFoodsConnectionAsync();
        return await db.FindAsync<FoodEntry>(entryId);
    }

    public async Task<FoodEntry?> DeleteFoodEntryAsync(int entryId)
    {
        var db = await GetFoodsConnectionAsync();
        var entry = await db.FindAsync<FoodEntry>(entryId);
        if (entry is null)
            return null;
        await db.DeleteAsync(entry);
        OnChanged();
        return entry;
    }

    /// <summary>Logs again, on <paramref name="to"/>, what was eaten at a meal of <paramref name="from"/>.</summary>
    public async Task<IReadOnlyList<FoodEntry>> CopyMealAsync(DateTime from, DateTime to, Meal meal, DateTime now)
    {
        var db = await GetFoodsConnectionAsync();
        var day = from.Date;
        var source = await db.Table<FoodEntry>().Where(e => e.Date == day && e.Meal == meal).OrderBy(e => e.Id).ToListAsync();
        var copies = source.Select(e => new FoodEntry
        {
            Date = to.Date, Meal = meal, FoodId = e.FoodId, Grams = e.Grams,
            Kcal = e.Kcal, Protein = e.Protein, Carbs = e.Carbs, Fat = e.Fat, CreatedAt = now, UpdatedAt = now,
        }).ToList();
        if (copies.Count > 0)
        {
            await db.InsertAllAsync(copies);
            OnChanged();
        }
        return copies;
    }

    /// <summary>Deletes everything logged at a meal of a day and returns it.</summary>
    public async Task<IReadOnlyList<FoodEntry>> ClearMealAsync(DateTime date, Meal meal)
    {
        var db = await GetFoodsConnectionAsync();
        var day = date.Date;
        var entries = await db.Table<FoodEntry>().Where(e => e.Date == day && e.Meal == meal).ToListAsync();
        if (entries.Count > 0)
        {
            await db.ExecuteAsync("DELETE FROM food_entries WHERE Date = ? AND Meal = ?", day, meal);
            OnChanged();
        }
        return entries;
    }

    /// <summary>A day's meals, in order, with what was eaten at each, and the water drunk.</summary>
    public async Task<NutritionDay> GetNutritionDayAsync(DateTime date)
    {
        var db = await GetFoodsConnectionAsync();
        var day = date.Date;
        var entries = await db.Table<FoodEntry>().Where(e => e.Date == day).OrderBy(e => e.CreatedAt).ToListAsync();
        var foods = new Dictionary<int, Food>();
        foreach (var id in entries.Select(e => e.FoodId).Distinct())
        {
            if (await db.FindAsync<Food>(id) is { } food)
                foods[id] = food;
        }
        var meals = Enum.GetValues<Meal>()
            .Select(m => new MealLog(m, entries.Where(e => e.Meal == m && foods.ContainsKey(e.FoodId)).Select(e => new LoggedFood(e, foods[e.FoodId])).ToList()))
            .ToList();
        var water = await db.ExecuteScalarAsync<int>("SELECT IFNULL(SUM(Milliliters), 0) FROM water WHERE Date = ?", day);
        return new NutritionDay(day, meals, water);
    }

    /// <summary>Calories eaten each day from <paramref name="from"/> to <paramref name="to"/>, days without anything included.</summary>
    public async Task<IReadOnlyList<DailyIntake>> GetDailyIntakeAsync(DateTime from, DateTime to)
    {
        var db = await GetFoodsConnectionAsync();
        var start = from.Date;
        var end = to.Date;
        var entries = await db.Table<FoodEntry>().Where(e => e.Date >= start && e.Date <= end).ToListAsync();
        var byDay = entries.GroupBy(e => e.Date.Date).ToDictionary(g => g.Key, g => (Kcal: g.Sum(e => e.Kcal), Protein: g.Sum(e => e.Protein)));
        return Enumerable.Range(0, (end - start).Days + 1)
            .Select(i => start.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var day)
                ? new DailyIntake(d, Math.Round(day.Kcal), Math.Round(day.Protein, 1))
                : new DailyIntake(d, 0))
            .ToList();
    }

    // Meal templates

    /// <summary>Saves what was eaten at a meal of a day as a template, to log it again in one tap.</summary>
    public async Task<MealTemplate> SaveMealTemplateAsync(string name, DateTime date, Meal meal, DateTime now)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new FonteException(FonteError.EmptyName, "A meal template needs a name.");
        var db = await GetFoodsConnectionAsync();
        var day = date.Date;
        var entries = await db.Table<FoodEntry>().Where(e => e.Date == day && e.Meal == meal).OrderBy(e => e.CreatedAt).ToListAsync();
        if (entries.Count == 0)
            throw new FonteException(FonteError.EmptyMeal, "Nothing was eaten at this meal.");
        var template = new MealTemplate { Name = Trim(name, 60), Meal = meal, CreatedAt = now };
        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(template);
            conn.InsertAll(entries.Select((e, i) => new MealTemplateItem { TemplateId = template.Id, FoodId = e.FoodId, Grams = e.Grams, Position = i }));
        });
        OnChanged();
        return template;
    }

    /// <summary>The meal templates with their foods; those of <paramref name="meal"/> first, then the most used.</summary>
    public async Task<IReadOnlyList<MealTemplateView>> GetMealTemplatesAsync(Meal? meal = null)
    {
        var db = await GetFoodsConnectionAsync();
        var templates = await db.Table<MealTemplate>().ToListAsync();
        var items = await db.Table<MealTemplateItem>().ToListAsync();
        var views = new List<MealTemplateView>();
        foreach (var template in templates
            .OrderByDescending(t => t.Meal == meal)
            .ThenByDescending(t => t.LastUsedAt ?? t.CreatedAt))
        {
            var logged = new List<LoggedFood>();
            foreach (var item in items.Where(i => i.TemplateId == template.Id).OrderBy(i => i.Position))
            {
                if (await db.FindAsync<Food>(item.FoodId) is not { } food)
                    continue;
                var entry = new FoodEntry { FoodId = food.Id };
                Fill(entry, food, item.Grams);
                logged.Add(new LoggedFood(entry, food));
            }
            views.Add(new MealTemplateView(template, logged));
        }
        return views;
    }

    /// <summary>Logs every food of a template at a meal of a day, with the foods' current nutrients.</summary>
    public async Task<IReadOnlyList<FoodEntry>> AddMealTemplateAsync(int templateId, DateTime date, Meal meal, DateTime now)
    {
        var db = await GetFoodsConnectionAsync();
        var template = await db.FindAsync<MealTemplate>(templateId)
            ?? throw new FonteException(FonteError.FoodNotFound, $"Meal template {templateId} not found.");
        var items = await db.Table<MealTemplateItem>().Where(i => i.TemplateId == templateId).OrderBy(i => i.Position).ToListAsync();
        var entries = new List<FoodEntry>();
        var foods = new List<Food>();
        foreach (var item in items)
        {
            if (await db.FindAsync<Food>(item.FoodId) is not { } food)
                continue;
            var entry = new FoodEntry { Date = date.Date, Meal = meal, FoodId = food.Id, CreatedAt = now, UpdatedAt = now };
            Fill(entry, food, item.Grams);
            entries.Add(entry);
            food.LastUsedAt = now;
            foods.Add(food);
        }
        template.LastUsedAt = now;
        await db.RunInTransactionAsync(conn =>
        {
            conn.InsertAll(entries);
            foreach (var food in foods)
                conn.Update(food);
            conn.Update(template);
        });
        OnChanged();
        return entries;
    }

    public async Task DeleteMealTemplateAsync(int templateId)
    {
        var db = await GetFoodsConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM meal_template_items WHERE TemplateId = ?", templateId);
            conn.Execute("DELETE FROM meal_templates WHERE Id = ?", templateId);
        });
        OnChanged();
    }

    // Recipes

    /// <summary>
    /// Creates or updates a dish from its ingredients. Its nutrients per 100 g come from the ingredients, divided by
    /// its weight once cooked (the sum of the ingredients when not given); a portion is that weight / servings.
    /// </summary>
    public async Task<Food> SaveRecipeAsync(int? recipeId, string name, IReadOnlyList<(int FoodId, double Grams)> ingredients,
        double? cookedGrams, int servings, DateTime now)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new FonteException(FonteError.EmptyName, "A dish needs a name.");
        if (ingredients.Count == 0)
            throw new FonteException(FonteError.EmptyRecipe, "A dish needs ingredients.");
        foreach (var (_, grams) in ingredients)
            CheckGrams(grams);
        var db = await GetFoodsConnectionAsync();
        var parts = new List<(Food Food, double Grams)>();
        foreach (var (foodId, grams) in ingredients)
            parts.Add((await FindFoodAsync(db, foodId), grams));

        var raw = parts.Sum(p => p.Grams);
        var total = cookedGrams is > 0 ? cookedGrams.Value : raw;
        var recipe = recipeId is { } id ? await FindFoodAsync(db, id) : new Food { Source = FoodSource.Recipe, CreatedAt = now };
        if (recipe.Source != FoodSource.Recipe)
            throw new ArgumentException("Not a dish.", nameof(recipeId));
        recipe.Name = Trim(name, 160);
        recipe.TotalGrams = Math.Round(total, 1);
        double Per100(Func<Food, double> nutrient) => Math.Round(parts.Sum(p => nutrient(p.Food) * p.Grams / 100) / total * 100, 2);
        double? Per100Optional(Func<Food, double?> nutrient) =>
            parts.All(p => nutrient(p.Food) is not null) ? Per100(f => nutrient(f) ?? 0) : null;
        recipe.Kcal = Per100(f => f.Kcal);
        recipe.Protein = Per100(f => f.Protein);
        recipe.Carbs = Per100(f => f.Carbs);
        recipe.Fat = Per100(f => f.Fat);
        recipe.Sugars = Per100Optional(f => f.Sugars);
        recipe.Fiber = Per100Optional(f => f.Fiber);
        recipe.Salt = Per100Optional(f => f.Salt);
        recipe.ServingGrams = Math.Round(total / Math.Max(1, servings), 1);
        recipe.ServingName = null;
        recipe.SearchText = SearchTextOf(recipe);

        await db.RunInTransactionAsync(conn =>
        {
            if (recipe.Id == 0)
                conn.Insert(recipe);
            else
                conn.Update(recipe);
            conn.Execute("DELETE FROM recipe_ingredients WHERE RecipeId = ?", recipe.Id);
            for (var i = 0; i < parts.Count; i++)
                conn.Insert(new RecipeIngredient { RecipeId = recipe.Id, FoodId = parts[i].Food.Id, Grams = parts[i].Grams, Position = i });
        });
        OnChanged();
        return recipe;
    }

    public async Task<IReadOnlyList<RecipePart>> GetRecipeAsync(int recipeId)
    {
        var db = await GetFoodsConnectionAsync();
        var items = await db.Table<RecipeIngredient>().Where(i => i.RecipeId == recipeId).OrderBy(i => i.Position).ToListAsync();
        var parts = new List<RecipePart>();
        foreach (var item in items)
        {
            if (await db.FindAsync<Food>(item.FoodId) is { } food)
                parts.Add(new RecipePart(food, item.Grams));
        }
        return parts;
    }

    // Water

    public async Task<WaterEntry> AddWaterAsync(DateTime date, int milliliters, DateTime now)
    {
        if (milliliters is <= 0 or > 3000)
            throw new FonteException(FonteError.InvalidQuantity, $"{milliliters} ml is not a drink.");
        var db = await GetFoodsConnectionAsync();
        var entry = new WaterEntry { Date = date.Date, Milliliters = milliliters, At = now };
        await db.InsertAsync(entry);
        OnChanged();
        return entry;
    }

    /// <summary>Takes back the last drink of the day (a tap too many); returns it, or null when there was none.</summary>
    public async Task<WaterEntry?> RemoveLastWaterAsync(DateTime date)
    {
        var db = await GetFoodsConnectionAsync();
        var day = date.Date;
        var last = await db.Table<WaterEntry>().Where(w => w.Date == day).OrderByDescending(w => w.Id).FirstOrDefaultAsync();
        if (last is null)
            return null;
        await db.DeleteAsync(last);
        OnChanged();
        return last;
    }

    // Helpers

    private static void Fill(FoodEntry entry, Food food, double grams)
    {
        entry.Grams = Math.Round(grams, 1);
        entry.Kcal = Energy.Scale(food.Kcal, grams);
        entry.Protein = Energy.Scale(food.Protein, grams);
        entry.Carbs = Energy.Scale(food.Carbs, grams);
        entry.Fat = Energy.Scale(food.Fat, grams);
    }

    private static void CheckGrams(double grams)
    {
        if (!double.IsFinite(grams) || grams <= 0 || grams > MaxGrams)
            throw new FonteException(FonteError.InvalidQuantity, $"{grams} g is not a quantity.");
    }

    /// <summary>Nutrients per 100 g must be possible: no more than 900 kcal, nor more than 100 g of them in all.</summary>
    private static void Validate(Food food)
    {
        double?[] optional = [food.Sugars, food.Fiber, food.Salt];
        var valid = food.Kcal is >= 0 and <= 950
            && food.Protein >= 0 && food.Carbs >= 0 && food.Fat >= 0
            && food.Protein + food.Carbs + food.Fat <= 101
            && optional.All(v => v is null || (v >= 0 && v <= 101))
            && (food.ServingGrams is null || (food.ServingGrams > 0 && food.ServingGrams <= MaxGrams));
        if (!valid)
            throw new FonteException(FonteError.InvalidFood, "These nutrients per 100 g are not possible.");
    }

    private async Task SaveFoodAsync(SQLiteAsyncConnection db, Food food)
    {
        food.SearchText = SearchTextOf(food);
        if (food.Id == 0)
            await db.InsertAsync(food);
        else
            await db.UpdateAsync(food);
        OnChanged();
    }

    private static string SearchTextOf(Food food) =>
        TextSearch.Normalize(string.Join(' ', new[] { food.Name, food.NameEn, food.Brand, food.Code }.OfType<string>()));

    private static bool StartsWith(Food food, string word) =>
        TextSearch.Normalize(food.Name).StartsWith(word, StringComparison.Ordinal)
        || (food.NameEn is { } en && TextSearch.Normalize(en).StartsWith(word, StringComparison.Ordinal));

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static async Task<Food> FindFoodAsync(SQLiteAsyncConnection db, int foodId) =>
        await db.FindAsync<Food>(foodId) ?? throw new FonteException(FonteError.FoodNotFound, $"Food {foodId} does not exist.");

    private static async Task<FoodEntry> FindEntryAsync(SQLiteAsyncConnection db, int entryId) =>
        await db.FindAsync<FoodEntry>(entryId) ?? throw new FonteException(FonteError.FoodNotFound, $"Entry {entryId} does not exist.");

    /// <summary>
    /// The connection, once the CIQUAL table is in the database. It is loaded the first time foods are needed, not
    /// at launch, so that people who don't log meals don't wait for it.
    /// </summary>
    private async Task<SQLiteAsyncConnection> GetFoodsConnectionAsync()
    {
        var db = await GetConnectionAsync();
        if (_catalogReady)
            return db;
        await _catalogLock.WaitAsync();
        try
        {
            if (!_catalogReady)
            {
                var known = (await db.QueryScalarsAsync<string>("SELECT Code FROM foods WHERE Source = ?", (int)FoodSource.Catalog)).ToHashSet();
                var missing = FoodCatalog.All.Where(c => !known.Contains(c.Code)).Select(c =>
                {
                    var food = new Food
                    {
                        Source = FoodSource.Catalog, Code = c.Code, Name = c.NameFr, NameEn = c.NameEn, Group = c.Group,
                        Kcal = c.Kcal, Protein = c.Protein, Carbs = c.Carbs, Fat = c.Fat, Sugars = c.Sugars, Fiber = c.Fiber, Salt = c.Salt,
                        CreatedAt = DateTime.MinValue,
                    };
                    food.SearchText = SearchTextOf(food);
                    return food;
                }).ToList();
                if (missing.Count > 0)
                    await db.InsertAllAsync(missing);
                _catalogReady = true;
            }
        }
        finally
        {
            _catalogLock.Release();
        }
        return db;
    }
}
