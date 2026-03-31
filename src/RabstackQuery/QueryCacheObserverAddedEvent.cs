namespace RabstackQuery;

/// <summary>
/// Emitted when an observer subscribes to a query.
/// </summary>
internal sealed class QueryCacheObserverAddedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
