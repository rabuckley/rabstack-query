namespace RabstackQuery;

/// <summary>
/// Base class providing a subscribe/unsubscribe pattern for typed listeners.
/// Thread-safe: listener mutations and lifecycle hooks are serialized via a
/// <see cref="Lock"/>, and callers iterate a snapshot via
/// <see cref="GetListenerSnapshot"/> to avoid concurrent-modification hazards
/// on the backing <see cref="HashSet{T}"/>.
/// </summary>
public class Subscribable<TListener> where TListener : Delegate
{
    private protected Subscribable() { }

    // Guards all reads and writes to _listeners. Held during Add, Remove,
    // Count checks, and the OnSubscribe/OnUnsubscribe callbacks so that the
    // first/last-listener transitions are atomic with the mutation.
    // Lock supports re-entrancy, so a callback that re-enters Subscribe on
    // the same Subscribable instance is safe.
    //
    // Lock ordering: _listenersLock → QueriesObserver._stateLock (never the
    // reverse). OnSubscribe/OnUnsubscribe acquire _stateLock while holding
    // _listenersLock.
    private readonly Lock _listenersLock = new();
    private readonly HashSet<TListener> _listeners = [];

    /// <summary>
    /// Returns the current listener count under the lock. Prefer
    /// <see cref="HasListeners"/> when only a boolean check is needed.
    /// </summary>
    public int ListenerCount
    {
        get { lock (_listenersLock) return _listeners.Count; }
    }

    /// <summary>
    /// Returns a point-in-time snapshot of the listener set, safe to iterate
    /// without holding the lock. The returned array is a copy — mutations to
    /// the listener set after the snapshot is taken are not reflected.
    ///
    /// All notification paths should use this instead of iterating <c>_listeners</c>
    /// directly. Matches the pattern in <c>Query.Dispatch</c> which snapshots
    /// <c>_observers</c> under <c>_observerLock</c> before iterating.
    /// </summary>
    internal TListener[] GetListenerSnapshot()
    {
        lock (_listenersLock)
        {
            return [.. _listeners];
        }
    }

    /// <summary>
    /// Subscribes a listener and returns an IDisposable that will unsubscribe when disposed.
    /// </summary>
    public IDisposable Subscribe(TListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        // Hold the lock across both the Add and the OnSubscribe callback so
        // that the first-subscriber check (ListenerCount == 1) inside
        // OnSubscribe sees a count consistent with this Add.
        lock (_listenersLock)
        {
            _listeners.Add(listener);
            OnSubscribe();
        }

        return new Subscription(this, listener);
    }

    /// <summary>
    /// Whether there are any listeners currently subscribed.
    /// </summary>
    public bool HasListeners
    {
        get { lock (_listenersLock) return _listeners.Count > 0; }
    }

    /// <summary>
    /// Called after a listener is added. Override to start timers or attach
    /// external event handlers when the first listener subscribes.
    /// </summary>
    /// <remarks>
    /// Called once per <see cref="Subscribe"/> invocation, after the listener
    /// has been added to the set. Called under <see cref="_listenersLock"/> so
    /// <see cref="ListenerCount"/> reflects the just-added listener. Check
    /// <see cref="HasListeners"/> or <see cref="ListenerCount"/> to detect
    /// the 0-to-1 transition.
    /// </remarks>
    protected virtual void OnSubscribe()
    {
    }

    /// <summary>
    /// Called after a listener is removed. Override to stop timers or detach
    /// external event handlers when the last listener unsubscribes.
    /// </summary>
    /// <remarks>
    /// Called once per disposal of a subscription, after the listener has been
    /// removed from the set. Called under <see cref="_listenersLock"/> so
    /// <see cref="ListenerCount"/> reflects the just-removed listener.
    /// </remarks>
    protected virtual void OnUnsubscribe()
    {
    }

    private sealed class Subscription : IDisposable
    {
        private Subscribable<TListener>? _owner;
        private TListener? _listener;

        public Subscription(Subscribable<TListener> owner, TListener listener)
        {
            this._owner = owner;
            this._listener = listener;
        }

        public void Dispose()
        {
            // Atomically claim ownership of the disposal. Only the thread that
            // gets a non-null owner proceeds; all others get null and bail.
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;

            lock (owner._listenersLock)
            {
                owner._listeners.Remove(_listener!);
                owner.OnUnsubscribe();
            }
            _listener = null;
        }
    }
}
