namespace RabstackQuery;

/// <summary>
/// Emitted when a new query is added to the cache.
/// </summary>
public sealed class QueryCacheQueryAddedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
