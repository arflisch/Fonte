namespace Fonte.Core.Models;

/// <summary>Reasons an operation is refused; the app shows a translated message for each.</summary>
public enum FonteError
{
    EmptyExerciseName,
    ExerciseNotFound,
    WorkoutNotFound,
    SetNotFound,
    IncompleteSet,
    EmptyName,
    ProgramNotFound,
    TemplateNotFound,
    InvalidBodyValue,
}

public sealed class FonteException(FonteError error, string message) : Exception(message)
{
    public FonteError Error { get; } = error;
}
