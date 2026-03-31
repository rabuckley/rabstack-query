namespace RabstackQuery;

/// <summary>
/// Emitted when a mutation is removed from the cache.
/// </summary>
public sealed class MutationCacheRemovedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
