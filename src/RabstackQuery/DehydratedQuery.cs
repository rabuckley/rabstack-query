namespace RabstackQuery;

/// <summary>
/// Snapshot of a single query's state for serialization.
/// </summary>
public sealed class DehydratedQuery
{
    public required string QueryHash { get; init; }

    public required QueryKey QueryKey { get; init; }

    public required DehydratedQueryState State { get; init; }

    public Meta? Meta { get; init; }

    public long DehydratedAt { get; init; }
}
