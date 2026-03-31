namespace RabstackQuery;

/// <summary>
/// Snapshot of a single mutation's state for serialization.
/// </summary>
public sealed class DehydratedMutation
{
    public QueryKey? MutationKey { get; init; }

    public required DehydratedMutationState State { get; init; }

    public Meta? Meta { get; init; }

    public MutationScope? Scope { get; init; }
}
