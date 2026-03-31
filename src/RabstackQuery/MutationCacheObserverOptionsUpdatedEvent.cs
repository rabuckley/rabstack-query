namespace RabstackQuery;

/// <summary>
/// Emitted when a mutation observer's options are updated.
/// </summary>
public sealed class MutationCacheObserverOptionsUpdatedEvent : MutationCacheNotifyEvent
{
    public Mutation? Mutation { get; init; }
}
