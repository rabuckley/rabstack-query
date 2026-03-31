namespace RabstackQuery;

/// <summary>
/// Emitted when an observer unsubscribes from a mutation.
/// </summary>
public sealed class MutationCacheObserverRemovedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
}
