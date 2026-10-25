using Fonte.Core.Models;

namespace Fonte.Core.Nutrition;

public sealed record Macros(double Kcal, double Protein, double Carbs, double Fat)
{
    public static readonly Macros Zero = new(0, 0, 0, 0);

    public static Macros Sum(IEnumerable<FoodEntry> entries)
    {
        double k = 0, p = 0, c = 0, f = 0;
        foreach (var e in entries)
        {
            k += e.Kcal;
            p += e.Protein;
            c += e.Carbs;
            f += e.Fat;
        }
        return new Macros(Math.Round(k), Math.Round(p, 1), Math.Round(c, 1), Math.Round(f, 1));
    }
}

public sealed record LoggedFood(FoodEntry Entry, Food Food);

public sealed record MealLog(Meal Meal, IReadOnlyList<LoggedFood> Foods)
{
    public Macros Totals => Macros.Sum(Foods.Select(f => f.Entry));
}

/// <summary>Everything eaten and drunk on a day.</summary>
public sealed record NutritionDay(DateTime Date, IReadOnlyList<MealLog> Meals, int WaterMl)
{
    public Macros Totals => Macros.Sum(Meals.SelectMany(m => m.Foods).Select(f => f.Entry));
}

public sealed record DailyIntake(DateTime Date, double Kcal);

public sealed record RecipePart(Food Food, double Grams);
