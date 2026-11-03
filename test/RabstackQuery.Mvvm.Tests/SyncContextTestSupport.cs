using System.Collections.Concurrent;

namespace RabstackQuery.Mvvm;

internal sealed class RecordingSynchronizationContext : SynchronizationContext
{
    private int _postCount;

    public int PostCount => Volatile.Read(ref _postCount);

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _postCount);
        d(state);
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public void Reset() => Interlocked.Exchange(ref _postCount, 0);
}

internal sealed class DeferredSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public int QueuedCount => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state) =>
        _queue.Enqueue((d, state));

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public bool RunNext()
    {
        if (!_queue.TryDequeue(out var item)) return false;
        item.Callback(item.State);
        return true;
    }

    public int DrainQueue()
    {
        var count = 0;
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
            count++;
        }
        return count;
    }
}

internal sealed class CrashRecordingSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Exception> _escaped = new();

    public IReadOnlyCollection<Exception> Escaped => _escaped;

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            d(state);
        }
        catch (Exception ex)
        {
            _escaped.Enqueue(ex);
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}

// ViewModel construction requires a context; these tests use inline callbacks without a UI dispatcher.
public abstract class SyncContextTestBase : IDisposable
{
    private readonly SynchronizationContext? _previousContext;

    protected SyncContextTestBase()
    {
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new RecordingSynchronizationContext());
    }

    public static T WithSyncContext<T>(SynchronizationContext context, Func<T> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public void Dispose() =>
        SynchronizationContext.SetSynchronizationContext(_previousContext);
}
