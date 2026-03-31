namespace RabstackQuery;

/// <summary>
/// Emitted when an observer unsubscribes from a query.
/// </summary>
internal sealed class QueryCacheObserverRemovedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
