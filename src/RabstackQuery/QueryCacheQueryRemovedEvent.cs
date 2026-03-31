namespace RabstackQuery;

/// <summary>
/// Emitted when a query is removed from the cache.
/// </summary>
public sealed class QueryCacheQueryRemovedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
