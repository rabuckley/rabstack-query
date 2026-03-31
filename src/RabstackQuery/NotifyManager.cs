namespace RabstackQuery;

/// <summary>
/// Batches and flushes observer notifications so that multiple state changes
/// within a single logical operation produce one consolidated notification pass.
/// </summary>
public sealed class NotifyManager : INotifyManager
{
    private readonly Lock _lock = new();
    private Queue<Action> _queue = new();
    private int _transactions;

    internal NotifyManager()
    {
    }

    public void Batch(Action callback)
    {
        lock (_lock)
        {
            _transactions++;
        }

        try
        {
            callback();
        }
        finally
        {
            lock (_lock)
            {
                _transactions--;

                if (_transactions == 0)
                {
                    Flush();
                }
            }
        }
    }

    public T Batch<T>(Func<T> callback)
    {
        lock (_lock)
        {
            _transactions++;
        }

        try
        {
            return callback();
        }
        finally
        {
            lock (_lock)
            {
                _transactions--;

                if (_transactions == 0)
                {
                    Flush();
                }
            }
        }
    }

    private void Flush()
    {
        // Called under _lock from Batch's finally block.
        if (_queue.Count == 0) return;

        var originalQueue = _queue;
        _queue = new Queue<Action>();

        // Flush synchronously. The previous implementation used Task.Run which
        // dispatched notifications to the thread pool, making observer callbacks
        // arrive on arbitrary threads. TanStack Query flushes synchronously by
        // default and lets consumers override via setScheduler. We match that
        // behavior here so that callers can rely on notifications completing
        // before the Batch call returns.
        while (originalQueue.Count > 0)
        {
            originalQueue.Dequeue()();
        }
    }
}
