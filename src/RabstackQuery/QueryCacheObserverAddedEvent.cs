namespace RabstackQuery;

public sealed class QueryCacheObserverAddedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
