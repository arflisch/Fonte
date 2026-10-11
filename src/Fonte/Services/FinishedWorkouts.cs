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

    /// <summary>
    /// The summary of that workout if it is the one just finished. It is not consumed: Shell may deliver the
    /// summary page's query more than once, and each load must find it.
    /// </summary>
    public WorkoutSummary? Find(int workoutId) => _last?.Workout.Id == workoutId ? _last : null;
}
