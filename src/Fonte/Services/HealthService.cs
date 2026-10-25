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
        var share = new NSSet(HKObjectType.WorkoutType, Type(HKQuantityTypeIdentifier.DietaryEnergyConsumed),
            Type(HKQuantityTypeIdentifier.DietaryProtein), Type(HKQuantityTypeIdentifier.DietaryCarbohydrates),
            Type(HKQuantityTypeIdentifier.DietaryFatTotal), Type(HKQuantityTypeIdentifier.DietaryWater));
        var read = new NSSet(BodyMass, Type(HKQuantityTypeIdentifier.ActiveEnergyBurned));
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

    /// <summary>
    /// Writes what was eaten (calories, protein, carbohydrates, fat) to Health. Each entry has its own sync
    /// identifier: saving it again (new quantity) replaces it rather than adding to it.
    /// </summary>
    public async Task SaveFoodAsync(Core.Models.FoodEntry entry, string name)
    {
        if (!IsActive)
            return;
#if IOS
        try
        {
            var at = MealTime(entry);
            var version = new NSNumber(entry.UpdatedAt.Ticks / TimeSpan.TicksPerSecond);
            HKQuantitySample Sample(HKQuantityTypeIdentifier type, HKUnit unit, double value, string part)
            {
                var metadata = new HKMetadata { FoodType = name };
                var dictionary = new NSMutableDictionary(metadata.Dictionary)
                {
                    [HKMetadataKey.SyncIdentifier] = new NSString($"fonte-food-{entry.Id}-{part}"),
                    [HKMetadataKey.SyncVersion] = version,
                };
                return HKQuantitySample.FromType(Type(type), HKQuantity.FromQuantity(unit, value), at, at, new HKMetadata(dictionary));
            }
            await _health.SaveObjectsAsync(
            [
                Sample(HKQuantityTypeIdentifier.DietaryEnergyConsumed, HKUnit.Kilocalorie, entry.Kcal, "kcal"),
                Sample(HKQuantityTypeIdentifier.DietaryProtein, HKUnit.Gram, entry.Protein, "protein"),
                Sample(HKQuantityTypeIdentifier.DietaryCarbohydrates, HKUnit.Gram, entry.Carbs, "carbs"),
                Sample(HKQuantityTypeIdentifier.DietaryFatTotal, HKUnit.Gram, entry.Fat, "fat"),
            ]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Food not saved to Health: {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Removes from Health what Fonte wrote for these entries (deleted from the journal).</summary>
    public async Task DeleteFoodsAsync(IEnumerable<Core.Models.FoodEntry> entries)
    {
        if (!IsActive)
            return;
#if IOS
        var ids = entries.SelectMany(e => new[] { "kcal", "protein", "carbs", "fat" }.Select(p => (NSObject)new NSString($"fonte-food-{e.Id}-{p}"))).ToArray();
        if (ids.Length == 0)
            return;
        try
        {
            var predicate = HKQuery.GetPredicateForMetadataKey(HKMetadataKey.SyncIdentifier, ids);
            foreach (var type in new[] { HKQuantityTypeIdentifier.DietaryEnergyConsumed, HKQuantityTypeIdentifier.DietaryProtein,
                         HKQuantityTypeIdentifier.DietaryCarbohydrates, HKQuantityTypeIdentifier.DietaryFatTotal })
            {
                var samples = await QuerySamplesAsync(Type(type), predicate);
                if (samples.Length > 0)
                    await _health.DeleteObjectsAsync(samples);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Food not removed from Health: {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task SaveWaterAsync(Core.Models.WaterEntry water)
    {
        if (!IsActive)
            return;
#if IOS
        try
        {
            var dictionary = new NSMutableDictionary { [HKMetadataKey.SyncIdentifier] = new NSString($"fonte-water-{water.Id}"), [HKMetadataKey.SyncVersion] = new NSNumber(1) };
            var at = ToDate(water.At);
            await _health.SaveObjectAsync(HKQuantitySample.FromType(Type(HKQuantityTypeIdentifier.DietaryWater),
                HKQuantity.FromQuantity(HKUnit.Liter, water.Milliliters / 1000.0), at, at, new HKMetadata(dictionary)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Water not saved to Health: {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task DeleteWaterAsync(Core.Models.WaterEntry water)
    {
        if (!IsActive)
            return;
#if IOS
        try
        {
            var predicate = HKQuery.GetPredicateForMetadataKey(HKMetadataKey.SyncIdentifier, [new NSString($"fonte-water-{water.Id}")]);
            var samples = await QuerySamplesAsync(Type(HKQuantityTypeIdentifier.DietaryWater), predicate);
            if (samples.Length > 0)
                await _health.DeleteObjectsAsync(samples);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Water not removed from Health: {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Calories burnt in activities on a day (watch, phone, other apps); 0 when unknown.</summary>
    public async Task<double> GetActiveEnergyAsync(DateTime day)
    {
        if (!IsActive)
            return 0;
#if IOS
        var result = new TaskCompletionSource<double>();
        try
        {
            var predicate = HKQuery.GetPredicateForSamples(ToDate(day.Date), ToDate(day.Date.AddDays(1)), HKQueryOptions.StrictStartDate);
            var query = new HKStatisticsQuery(Type(HKQuantityTypeIdentifier.ActiveEnergyBurned), predicate, HKStatisticsOptions.CumulativeSum,
                (_, statistics, _) => result.TrySetResult(statistics?.SumQuantity()?.GetDoubleValue(HKUnit.Kilocalorie) ?? 0));
            _health.ExecuteQuery(query);
            return Math.Round(await result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Active energy not read from Health: {ex}");
            return 0;
        }
#else
        await Task.CompletedTask;
        return 0;
#endif
    }

#if IOS
    private static HKQuantityType Type(HKQuantityTypeIdentifier identifier) => HKQuantityType.Create(identifier)!;

    /// <summary>Entries of today are dated when they were logged; those of another day at a usual time of the meal.</summary>
    private static NSDate MealTime(Core.Models.FoodEntry entry)
    {
        if (entry.CreatedAt.Date == entry.Date)
            return ToDate(entry.CreatedAt);
        var hour = entry.Meal switch
        {
            Core.Models.Meal.Breakfast => 8,
            Core.Models.Meal.Lunch => 12.5,
            Core.Models.Meal.Dinner => 19.5,
            _ => 16,
        };
        return ToDate(entry.Date.AddHours(hour));
    }

    private Task<HKObject[]> QuerySamplesAsync(HKSampleType type, NSPredicate predicate)
    {
        var result = new TaskCompletionSource<HKObject[]>();
        var query = new HKSampleQuery(type, predicate, 0, [], (_, samples, _) => result.TrySetResult(samples?.Cast<HKObject>().ToArray() ?? []));
        _health.ExecuteQuery(query);
        return result.Task;
    }

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
