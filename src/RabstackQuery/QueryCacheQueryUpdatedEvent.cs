namespace RabstackQuery;

public sealed class QueryCacheQueryUpdatedEvent : QueryCacheNotifyEvent
{
    public required Query Query { get; init; }
    public required DispatchAction Action { get; init; }
}
