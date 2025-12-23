namespace RabstackQuery;

public sealed class QueryCacheObserverOptionsUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
