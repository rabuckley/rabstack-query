namespace RabstackQuery;

public sealed class MutationCacheUpdatedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
    public required string ActionType { get; init; }
}
