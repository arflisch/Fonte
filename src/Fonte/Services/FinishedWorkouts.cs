using Fonte.Core.Training;

namespace Fonte.Services;

/// <summary>
/// Hands what finishing a workout returned (the targets that went up) to the summary shown right after: the
/// summary reloads the workout from the database, which doesn't keep them.
/// </summary>
public sealed class FinishedWorkouts
{
    private WorkoutSummary? _last;

    public void Remember(WorkoutSummary summary) => _last = summary;

    /// <summary>The summary of that workout if it was just finished, once.</summary>
    public WorkoutSummary? Take(int workoutId)
    {
        var last = _last;
        if (last?.Workout.Id != workoutId)
            return null;
        _last = null;
        return last;
    }
}
