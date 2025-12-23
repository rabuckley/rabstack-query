namespace RabstackQuery;

public sealed class MutationCacheAddedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
