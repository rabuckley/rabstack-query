namespace RabstackQuery;

/// <summary>
/// Filters for matching mutations in bulk operations like
/// <see cref="QueryClient.IsMutating"/>.
/// All specified filters are combined with AND logic.
/// </summary>
public sealed class MutationFilters
{
    /// <summary>
    /// Filter by mutation key. When <see cref="Exact"/> is false (default),
    /// this performs a prefix match.
    /// </summary>
    public QueryKey? MutationKey { get; init; }

    /// <summary>
    /// When true, only mutations whose key matches exactly will pass.
    /// Default is false (prefix match).
    /// </summary>
    public bool Exact { get; init; }

    /// <summary>
    /// Filter by mutation status (Idle, Pending, Success, Error).
    /// </summary>
    public MutationStatus? Status { get; init; }

    /// <summary>
    /// Arbitrary predicate applied after all other filters.
    /// </summary>
    public Func<Mutation, bool>? Predicate { get; init; }
}
