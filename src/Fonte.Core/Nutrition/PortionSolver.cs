namespace Fonte.Core.Nutrition;

/// <summary>A food chosen for a meal, its nutrients per 100 g and the quantities that make sense for it.</summary>
public sealed record PlanFood(int FoodId, string Name, double Kcal, double Protein, double Carbs, double Fat, double MinGrams, double MaxGrams);

/// <summary>Grams of each food, and what the meal then brings.</summary>
public sealed record MealPlan(IReadOnlyList<(PlanFood Food, double Grams)> Portions, Macros Totals);

/// <summary>
/// Works out how much of each chosen food to eat so a meal reaches its calories and protein: a weighted least
/// squares fit of the grams (calories, protein, carbohydrates and fat as relative errors, protein counting
/// twice), each food kept between its own bounds, solved by projected gradient descent.
/// </summary>
public static class PortionSolver
{
    /// <summary>Sensible bounds for a food: oils and nuts in small amounts, vegetables freely.</summary>
    public static (double Min, double Max) Bounds(double kcal, double protein, double fat)
    {
        if (fat >= 50)
            return (5, 30);
        if (kcal >= 400)
            return (10, 80);
        if (kcal < 60)
            return (50, 400);
        if (protein >= 15)
            return (60, 300);
        return (40, 350);
    }

    public static MealPlan Solve(IReadOnlyList<PlanFood> foods, Macros target)
    {
        if (foods.Count == 0)
            return new MealPlan([], Macros.Zero);

        // Each nutrient as a share of its target, so calories and grams weigh alike; a missing target counts less.
        var weights = new[] { 1.0, 2.0, target.Carbs > 0 ? 0.5 : 0, target.Fat > 0 ? 0.5 : 0 };
        var goal = new[] { target.Kcal, target.Protein, target.Carbs, target.Fat };
        var scale = goal.Select(g => g > 0 ? 1 / g : 0).ToArray();
        double[] Per(PlanFood f) => [f.Kcal / 100, f.Protein / 100, f.Carbs / 100, f.Fat / 100];
        var per = foods.Select(Per).ToArray();

        var grams = foods.Select(f => Math.Clamp(100, f.MinGrams, f.MaxGrams)).ToArray();
        var step = 2000.0;
        for (var iteration = 0; iteration < 4000; iteration++)
        {
            var residual = new double[4];
            for (var n = 0; n < 4; n++)
                residual[n] = (foods.Select((_, i) => per[i][n] * grams[i]).Sum() - goal[n]) * scale[n];
            var moved = 0.0;
            for (var i = 0; i < foods.Count; i++)
            {
                var gradient = 0.0;
                for (var n = 0; n < 4; n++)
                    gradient += 2 * weights[n] * residual[n] * per[i][n] * scale[n];
                var next = Math.Clamp(grams[i] - step * gradient, foods[i].MinGrams, foods[i].MaxGrams);
                moved += Math.Abs(next - grams[i]);
                grams[i] = next;
            }
            if (moved < 0.01)
                break;
            if (iteration % 500 == 499)
                step /= 2;
        }

        var rounded = grams.Select((g, i) => Math.Clamp(Math.Round(g / 5) * 5, foods[i].MinGrams, foods[i].MaxGrams)).ToArray();
        var totals = new Macros(
            foods.Select((f, i) => f.Kcal * rounded[i] / 100).Sum(),
            foods.Select((f, i) => f.Protein * rounded[i] / 100).Sum(),
            foods.Select((f, i) => f.Carbs * rounded[i] / 100).Sum(),
            foods.Select((f, i) => f.Fat * rounded[i] / 100).Sum());
        return new MealPlan(foods.Select((f, i) => (f, rounded[i])).ToList(), totals);
    }

    /// <summary>What a food is in a meal, to word the preparation.</summary>
    public enum Role
    {
        Protein,
        Starch,
        Vegetable,
        Fat,
        Other,
    }

    public static Role RoleOf(double kcal, double protein, double carbs, double fat) =>
        fat >= 50 ? Role.Fat
        : protein >= 12 && protein * 4 >= carbs * 4 ? Role.Protein
        : kcal < 60 ? Role.Vegetable
        : carbs >= 15 ? Role.Starch
        : Role.Other;
}
