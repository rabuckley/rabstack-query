namespace RabstackQuery;

public sealed class MutationCacheRemovedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
