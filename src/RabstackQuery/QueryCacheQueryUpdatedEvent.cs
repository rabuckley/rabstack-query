namespace RabstackQuery;

/// <summary>
/// Emitted when a query's state changes (fetch, error, success, invalidation, etc.).
/// </summary>
public sealed class QueryCacheQueryUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required DispatchAction Action { get; init; }
}
