namespace RabstackQuery;

public sealed class MutationCacheObserverAddedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
