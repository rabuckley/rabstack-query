namespace RabstackQuery;

/// <summary>
/// Emitted when an observer subscribes to a mutation.
/// </summary>
public sealed class MutationCacheObserverAddedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
