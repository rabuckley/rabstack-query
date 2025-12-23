namespace RabstackQuery;

public sealed class QueryCacheQueryAddedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
