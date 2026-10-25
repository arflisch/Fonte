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
/// <param name="Adaptive">Each week, maintenance is measured from what was eaten and how the weight moved.</param>
/// <param name="MeasuredMaintenance">The last maintenance measured, used instead of the formula when adaptive.</param>
/// <param name="AdaptedOn">When it was measured.</param>
/// <param name="TrainingDays">More calories on training days, fewer on rest days, the same over the week.</param>
public sealed record NutritionProfile(
    Sex Sex, int BirthYear, double HeightCm, ActivityLevel Activity, WeightGoal Goal, double WeeklyChangeKg, double ProteinPerKg,
    bool Adaptive = false, double? MeasuredMaintenance = null, DateTime? AdaptedOn = null, bool TrainingDays = false);

/// <summary>Maintenance worked out from real life: calories eaten, minus the energy of the weight gained.</summary>
/// <param name="LoggedDays">Days of the window whose meals were logged.</param>
/// <param name="KgPerWeek">The weight trend over the window.</param>
public sealed record MaintenanceEstimate(double Kcal, int LoggedDays, double KgPerWeek);

/// <param name="Bmr">Resting metabolism (Mifflin-St Jeor).</param>
/// <param name="Maintenance">Calories that keep the weight stable.</param>
/// <param name="Kcal">Daily calories to aim for.</param>
/// <param name="IsFloored">The deficit asked for would go under the safe minimum: the target was raised to it.</param>
/// <param name="IsMeasured">Maintenance comes from the weeks logged rather than from the formula.</param>
public sealed record NutritionTargets(
    double Bmr, double Maintenance, double Kcal, double Protein, double Carbs, double Fat, bool IsFloored, bool IsMeasured = false)
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
        var formula = bmr * ActivityFactor(profile.Activity);
        // A measure far off the formula is more likely a logging gap than a metabolism: keep it within 25 %.
        var measured = profile.Adaptive && profile.MeasuredMaintenance is { } m ? Math.Clamp(m, formula * 0.75, formula * 1.25) : (double?)null;
        var maintenance = measured ?? formula;
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
        return new NutritionTargets(Math.Round(bmr), Math.Round(maintenance), kcal, protein, carbs, fat, floored, measured is not null);
    }

    /// <summary>When the goal weight would be reached at this pace; null when going the other way or not moving.</summary>
    /// <summary>Share of the day's calories added on a training day (and taken back over the rest days).</summary>
    public const double TrainingDayShare = 0.1;

    /// <summary>
    /// The targets of one day when training days get more: +10 % of calories on a training day, as carbohydrates,
    /// and less on rest days so the week stays the same for <paramref name="sessionsPerWeek"/> workouts.
    /// </summary>
    public static NutritionTargets ForDay(NutritionProfile profile, NutritionTargets targets, bool training, int sessionsPerWeek)
    {
        if (!profile.TrainingDays)
            return targets;
        var sessions = Math.Clamp(sessionsPerWeek, 1, 6);
        var boost = Math.Round(targets.Kcal * TrainingDayShare / 10) * 10;
        var change = training ? boost : -Math.Round(boost * sessions / (7 - sessions) / 10) * 10;
        var kcal = Math.Max(targets.Kcal + change, MinimumKcal(profile.Sex));
        return targets with { Kcal = kcal, Carbs = Math.Max(0, Math.Round(targets.Carbs + (kcal - targets.Kcal) / 4)) };
    }

    /// <summary>Days looked back on to measure maintenance, and how many of them must have their meals logged.</summary>
    public const int AdaptiveWindowDays = 21, AdaptiveLoggedDays = 10;

    /// <summary>A day under this many calories is taken as partly logged and left out of the measure.</summary>
    public const double LoggedDayMinimum = 800;

    /// <summary>
    /// Measures maintenance over the 21 days before <paramref name="today"/>: the calories eaten on the days fully
    /// logged, corrected by the weight trend (least squares over the weighings, 7,700 kcal per kg). Null until there
    /// are 10 logged days and 3 weighings at least 10 days apart.
    /// </summary>
    public static MaintenanceEstimate? MeasureMaintenance(
        IReadOnlyList<DailyIntake> days, IReadOnlyList<(DateTime Date, double Kilograms)> weights, DateTime today)
    {
        var from = today.Date.AddDays(-AdaptiveWindowDays);
        var to = today.Date.AddDays(-1);
        var logged = days.Where(d => d.Date >= from && d.Date <= to && d.Kcal >= LoggedDayMinimum).ToList();
        var weighed = weights.Where(w => w.Date >= from && w.Date <= today.Date).OrderBy(w => w.Date).ToList();
        if (logged.Count < AdaptiveLoggedDays || weighed.Count < 3 || (weighed[^1].Date - weighed[0].Date).TotalDays < 10)
            return null;

        var xs = weighed.Select(w => (w.Date - from).TotalDays).ToList();
        var meanX = xs.Average();
        var meanY = weighed.Average(w => w.Kilograms);
        var slope = xs.Zip(weighed).Sum(p => (p.First - meanX) * (p.Second.Kilograms - meanY)) / xs.Sum(x => (x - meanX) * (x - meanX));
        var kcal = logged.Average(d => d.Kcal) - slope * KcalPerKg;
        return new MaintenanceEstimate(Math.Round(Math.Clamp(kcal, 1000, 6000) / 10) * 10, logged.Count, Math.Round(slope * 7, 2));
    }

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
