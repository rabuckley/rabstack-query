namespace RabstackQuery;

/// <summary>
/// Emitted when an observer's options are updated.
/// </summary>
internal sealed class QueryCacheObserverOptionsUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required IQueryObserver Observer { get; init; }
}
