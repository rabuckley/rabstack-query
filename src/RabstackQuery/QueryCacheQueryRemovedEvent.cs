namespace RabstackQuery;

public sealed class QueryCacheQueryRemovedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
