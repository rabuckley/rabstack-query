namespace RabstackQuery;

public sealed class MutationCacheObserverOptionsUpdatedEvent : MutationCacheNotifyEvent
{
    public Mutation? Mutation { get; init; }
}
