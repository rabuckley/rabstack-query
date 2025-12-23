namespace RabstackQuery;

public sealed class MutationCacheObserverRemovedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
