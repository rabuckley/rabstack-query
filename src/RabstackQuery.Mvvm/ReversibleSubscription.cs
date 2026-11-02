namespace RabstackQuery.Mvvm;

// Owns the observer subscription behind each wrapper's Subscribed property. Query, infinite
// query and mutation wrappers share it because their pause and resume rules change together.
internal sealed class ReversibleSubscription<TResult>
{
    private readonly object _owner;
    private readonly Func<Action<TResult>, IDisposable> _subscribe;
    private readonly Func<TResult> _currentResult;
    private readonly Action<TResult> _apply;
    private readonly SynchronizationContext _syncContext;
    private IDisposable? _subscription;

    // Changes on every subscribe and unsubscribe. A queued post from an earlier subscription
    // carries a result older than the one the resume applied, so it must not run.
    private int _generation;

    // Dispose may run off the UI thread, so posted callbacks need a volatile guard.
    private volatile bool _disposed;

    public ReversibleSubscription(
        object owner,
        SynchronizationContext syncContext,
        Func<Action<TResult>, IDisposable> subscribe,
        Func<TResult> currentResult,
        Action<TResult> apply)
    {
        _owner = owner;
        _syncContext = syncContext;
        _subscribe = subscribe;
        _currentResult = currentResult;
        _apply = apply;
    }

    public bool IsActive => Volatile.Read(ref _subscription) is not null;

    /// <returns><see langword="true"/> if the state changed; otherwise, <see langword="false"/>.</returns>
    public bool SetActive(bool active)
    {
        if (active)
        {
            ObjectDisposedException.ThrowIf(_disposed, _owner);
            return Activate();
        }

        return Deactivate();
    }

    // A detached owner has no listener, so nothing else brings its state up to date with the observer.
    public void RefreshIfInactive()
    {
        if (_disposed || IsActive) return;

        _apply(_currentResult());
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _subscription, null)?.Dispose();
    }

    private bool Activate()
    {
        if (IsActive) return false;

        var generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _subscription, _subscribe(result => Post(result, generation)));

        // Dispose can run off the UI thread and miss a subscription that was still being created.
        if (_disposed)
        {
            Interlocked.Exchange(ref _subscription, null)?.Dispose();
            return false;
        }

        _apply(_currentResult());
        return true;
    }

    private bool Deactivate()
    {
        var subscription = Interlocked.Exchange(ref _subscription, null);
        if (subscription is null) return false;

        Interlocked.Increment(ref _generation);
        subscription.Dispose();
        return true;
    }

    private void Post(TResult result, int generation) =>
        _syncContext.Post(_ =>
        {
            if (_disposed || generation != Volatile.Read(ref _generation)) return;
            _apply(result);
        }, null);
}
