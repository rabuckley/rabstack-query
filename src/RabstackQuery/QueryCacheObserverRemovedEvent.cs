namespace RabstackQuery;

public sealed class QueryCacheObserverRemovedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
