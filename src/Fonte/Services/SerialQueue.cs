namespace Fonte.Services;

/// <summary>
/// Runs database writes one after the other, in the order they were asked for: typing "12" in a field saves "1"
/// then "12", never the other way round.
/// </summary>
public sealed class SerialQueue
{
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Func<Task> job) => Enqueue(async () =>
    {
        await job();
        return true;
    });

    public Task<T> Enqueue<T>(Func<Task<T>> job)
    {
        var run = RunAfterAsync(_tail, job);
        _tail = run;
        return run;
    }

    /// <summary>Completes once everything queued so far has run (successfully or not).</summary>
    public Task WhenIdle() => _tail.ContinueWith(static _ => { }, TaskScheduler.Default);

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> job)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // Reported to whoever queued it.
        }
        return await job();
    }
}
