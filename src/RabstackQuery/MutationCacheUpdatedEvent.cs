namespace RabstackQuery;

/// <summary>
/// Emitted when a mutation's state changes.
/// </summary>
public sealed class MutationCacheUpdatedEvent : MutationCacheNotifyEvent
{
    public required Mutation Mutation { get; init; }
    public required string ActionType { get; init; }
}
