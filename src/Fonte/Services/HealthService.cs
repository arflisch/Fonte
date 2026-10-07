#if IOS
using Foundation;
using HealthKit;
#endif
using Fonte.Core.Data;

namespace Fonte.Services;

/// <summary>
/// Apple Health (Fonte Pro, iPhone only): finished workouts are saved as strength training sessions, and the
/// body weight recorded by a scale or another app comes into Fonte. Nothing else is shared.
/// </summary>
public sealed class HealthService(FonteStore store, AppSettings settings, ProService pro)
{
    /// <summary>How far back weighings are taken from Health.</summary>
    private const int ImportDays = 365;

#if IOS
    private readonly HKHealthStore _health = new();
#endif

    public static bool IsSupported
    {
        get
        {
#if IOS
            return HKHealthStore.IsHealthDataAvailable;
#else
            return false;
#endif
        }
    }

    /// <summary>The user turned it on, and Fonte Pro is unlocked.</summary>
    public bool IsActive => IsSupported && settings.HealthEnabled && pro.IsUnlocked;

    /// <summary>Asks iOS for access (the system sheet only shows once). True when the user can go on.</summary>
    public async Task<bool> RequestAccessAsync()
    {
#if IOS
        if (!IsSupported)
            return false;
        var share = new NSSet(HKObjectType.WorkoutType);
        var read = new NSSet(BodyMass);
        var (success, _) = await _health.RequestAuthorizationToShareAsync(share, read);
        return success;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    /// <summary>Saves a finished workout to Health; silently skipped when Health is off or refused.</summary>
    public async Task SaveWorkoutAsync(DateTime start, DateTime end)
    {
        if (!IsActive || end <= start)
            return;
#if IOS
        try
        {
            var configuration = new HKWorkoutConfiguration
            {
                ActivityType = HKWorkoutActivityType.TraditionalStrengthTraining,
                LocationType = HKWorkoutSessionLocationType.Indoor,
            };
            var builder = new HKWorkoutBuilder(_health, configuration, null);
            await builder.BeginCollectionAsync(ToDate(start));
            await builder.EndCollectionAsync(ToDate(end));
            await builder.FinishWorkoutAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Workout not saved to Health: {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// Brings the weighings of the last year from Health into Fonte, one per day, without touching the days
    /// already weighed in Fonte. Returns how many were added.
    /// </summary>
    public async Task<int> ImportWeightsAsync()
    {
        if (!IsActive)
            return 0;
#if IOS
        try
        {
            var known = (await store.GetWeightsAsync()).Select(w => w.Date.Date).ToHashSet();
            var samples = await QueryWeightsAsync(DateTime.Now.AddDays(-ImportDays), DateTime.Now);
            var added = 0;
            // Latest weighing of each day.
            foreach (var day in samples.GroupBy(s => s.Date.Date).Where(g => !known.Contains(g.Key)))
            {
                await store.SaveWeightAsync(day.Key, day.MaxBy(s => s.Date).Kilograms);
                added++;
            }
            return added;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Weights not read from Health: {ex}");
            return 0;
        }
#else
        await Task.CompletedTask;
        return 0;
#endif
    }

#if IOS
    private static HKQuantityType BodyMass => HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass)!;

    private static NSDate ToDate(DateTime date) =>
        (NSDate)(date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Local) : date);

    private Task<List<(DateTime Date, double Kilograms)>> QueryWeightsAsync(DateTime from, DateTime to)
    {
        var result = new TaskCompletionSource<List<(DateTime, double)>>();
        var predicate = HKQuery.GetPredicateForSamples(ToDate(from), ToDate(to), HKQueryOptions.None);
        var query = new HKSampleQuery(BodyMass, predicate, 0, [], (_, samples, _) =>
        {
            var kilogram = HKUnit.FromGramUnit(HKMetricPrefix.Kilo);
            var weights = (samples ?? [])
                .OfType<HKQuantitySample>()
                .Select(s => (((DateTime)s.EndDate).ToLocalTime(), Math.Round(s.Quantity.GetDoubleValue(kilogram), 1)))
                .Where(w => w.Item2 is >= 20 and <= 400)
                .ToList();
            result.TrySetResult(weights);
        });
        _health.ExecuteQuery(query);
        return result.Task;
    }
#endif
}
