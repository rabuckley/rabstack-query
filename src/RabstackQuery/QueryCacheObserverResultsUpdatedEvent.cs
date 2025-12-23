namespace RabstackQuery;

public sealed class QueryCacheObserverResultsUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
