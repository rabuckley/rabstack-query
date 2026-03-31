namespace RabstackQuery;

/// <summary>
/// Emitted when an observer's computed result changes.
/// </summary>
public sealed class QueryCacheObserverResultsUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
}
