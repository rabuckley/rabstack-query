namespace RabstackQuery;

/// <summary>
/// Emitted when a new mutation is added to the cache.
/// </summary>
public sealed class MutationCacheAddedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
