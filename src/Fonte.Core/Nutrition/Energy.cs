namespace Fonte.Core.Nutrition;

public enum Sex
{
    Male = 0,
    Female = 1,
}

/// <summary>Daily activity outside the workouts, with the multiplier of the resting metabolism it means.</summary>
public enum ActivityLevel
{
    /// <summary>Desk job, little walking (× 1.2).</summary>
    Sedentary = 0,

    /// <summary>On one's feet part of the day, or 1–3 workouts a week (× 1.375).</summary>
    Light = 1,

    /// <summary>3–5 workouts a week (× 1.55).</summary>
    Moderate = 2,

    /// <summary>6–7 workouts a week, or an active job (× 1.725).</summary>
    Active = 3,

    /// <summary>Physical job and daily training (× 1.9).</summary>
    VeryActive = 4,
}

public enum WeightGoal
{
    Lose = 0,
    Maintain = 1,
    Gain = 2,
}

/// <param name="WeeklyChangeKg">How fast to lose or gain, in kilograms per week (ignored when maintaining).</param>
/// <param name="ProteinPerKg">Grams of protein per kilogram of body weight (1.6 to 2.2 for strength training).</param>
public sealed record NutritionProfile(
    Sex Sex, int BirthYear, double HeightCm, ActivityLevel Activity, WeightGoal Goal, double WeeklyChangeKg, double ProteinPerKg);

/// <param name="Bmr">Resting metabolism (Mifflin-St Jeor).</param>
/// <param name="Maintenance">Calories that keep the weight stable.</param>
/// <param name="Kcal">Daily calories to aim for.</param>
/// <param name="IsFloored">The deficit asked for would go under the safe minimum: the target was raised to it.</param>
public sealed record NutritionTargets(double Bmr, double Maintenance, double Kcal, double Protein, double Carbs, double Fat, bool IsFloored)
{
    /// <summary>Calories below (negative) or above maintenance.</summary>
    public double Balance => Kcal - Maintenance;
}

/// <summary>Daily needs, and how they turn into a target to lose, keep or gain weight.</summary>
public static class Energy
{
    /// <summary>Energy in a kilogram of body fat: a 500 kcal daily deficit is about 0.45 kg a week.</summary>
    public const double KcalPerKg = 7700;

    /// <summary>Share of the calories given to fat.</summary>
    public const double FatShare = 0.25;

    /// <summary>Losing more than this per week costs muscle; gaining more, mostly fat.</summary>
    public const double MaxLossPerWeek = 1.0, MaxGainPerWeek = 0.5;

    public static double ActivityFactor(ActivityLevel level) => level switch
    {
        ActivityLevel.Sedentary => 1.2,
        ActivityLevel.Light => 1.375,
        ActivityLevel.Moderate => 1.55,
        ActivityLevel.Active => 1.725,
        _ => 1.9,
    };

    /// <summary>Calories that are not gone under without medical follow-up.</summary>
    public static double MinimumKcal(Sex sex) => sex == Sex.Female ? 1200 : 1500;

    /// <summary>Mifflin-St Jeor resting metabolism.</summary>
    public static double Bmr(Sex sex, int age, double heightCm, double weightKg) =>
        10 * weightKg + 6.25 * heightCm - 5 * age + (sex == Sex.Male ? 5 : -161);

    public static int Age(int birthYear, DateTime today) => Math.Clamp(today.Year - birthYear, 14, 100);

    public static NutritionTargets Targets(NutritionProfile profile, double weightKg, DateTime today)
    {
        var bmr = Bmr(profile.Sex, Age(profile.BirthYear, today), profile.HeightCm, weightKg);
        var maintenance = bmr * ActivityFactor(profile.Activity);
        var daily = profile.Goal switch
        {
            WeightGoal.Lose => -Math.Clamp(profile.WeeklyChangeKg, 0, MaxLossPerWeek) * KcalPerKg / 7,
            WeightGoal.Gain => Math.Clamp(profile.WeeklyChangeKg, 0, MaxGainPerWeek) * KcalPerKg / 7,
            _ => 0,
        };
        var kcal = maintenance + daily;
        var floored = kcal < MinimumKcal(profile.Sex);
        if (floored)
            kcal = MinimumKcal(profile.Sex);
        kcal = Math.Round(kcal / 10) * 10;

        var protein = Math.Round(Math.Clamp(profile.ProteinPerKg, 0.8, 3) * weightKg);
        var fat = Math.Round(kcal * FatShare / 9);
        var carbs = Math.Max(0, Math.Round((kcal - protein * 4 - fat * 9) / 4));
        return new NutritionTargets(Math.Round(bmr), Math.Round(maintenance), kcal, protein, carbs, fat, floored);
    }

    /// <summary>When the goal weight would be reached at this pace; null when going the other way or not moving.</summary>
    public static DateTime? GoalDate(double weightKg, double goalKg, double weeklyChangeKg, DateTime today)
    {
        var gap = goalKg - weightKg;
        if (Math.Abs(gap) < 0.05)
            return today.Date;
        if (weeklyChangeKg <= 0)
            return null;
        return today.Date.AddDays(Math.Ceiling(Math.Abs(gap) / weeklyChangeKg * 7));
    }

    /// <summary>Nutrients of <paramref name="grams"/> of a food given per 100 g.</summary>
    public static double Scale(double per100, double grams) => Math.Round(per100 * grams / 100, 1);
}
