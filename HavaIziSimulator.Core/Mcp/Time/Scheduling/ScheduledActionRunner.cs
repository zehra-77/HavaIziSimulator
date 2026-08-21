using HavaIziSimulator.Mcp.Time.Models;

namespace HavaIziSimulator.Mcp.Time.Scheduling;

public sealed class ScheduledActionRunner : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _pending = [];
    private readonly object _sync = new();

    public int PendingCount { get { lock (_sync) return _pending.Count; } }

    public void Schedule(
        ScheduledActionPayloadDto action,
        Func<ScheduledActionPayloadDto, CancellationToken, Task> execute,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            _shutdown.Token, cancellationToken);
        Task task = RunAsync(action, execute, linked);
        lock (_sync) _pending.Add(task);
        _ = task.ContinueWith(completed =>
        {
            lock (_sync) _pending.Remove(completed);
            _ = completed.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static async Task RunAsync(
        ScheduledActionPayloadDto action,
        Func<ScheduledActionPayloadDto, CancellationToken, Task> execute,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            await Task.Delay(TimeSpan.FromSeconds(action.DelaySeconds), linked.Token);
            await execute(action, linked.Token);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
