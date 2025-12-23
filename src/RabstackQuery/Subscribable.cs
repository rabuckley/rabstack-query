namespace RabstackQuery;

public class Subscribable<TListener> where TListener : Delegate
{
    private readonly HashSet<TListener> _listeners = [];

    public IReadOnlyCollection<TListener> Listeners => _listeners;

    /// <summary>
    /// Subscribes a listener and returns an IDisposable that will unsubscribe when disposed.
    /// </summary>
    public IDisposable Subscribe(TListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        _listeners.Add(listener);
        OnSubscribe();

        return new Subscription(this, listener);
    }

    /// <summary>
    /// Whether there are any listeners currently subscribed.
    /// </summary>
    public bool HasListeners() => _listeners.Count > 0;

    protected virtual void OnSubscribe()
    {
    }

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
            if (_owner is null || _listener is null)
                return;

            _owner._listeners.Remove(_listener);
            _owner.OnUnsubscribe();

            _owner = null;
            _listener = null;
        }
    }
}
