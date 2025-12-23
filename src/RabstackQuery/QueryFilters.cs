namespace RabstackQuery;

/// <summary>
/// Filters for matching queries in bulk operations like <c>InvalidateQueries</c>,
/// <c>RefetchQueries</c>, <c>RemoveQueries</c>, etc.
/// All specified filters are combined with AND logic.
/// </summary>
public class QueryFilters
{
    /// <summary>
    /// Filter by query key. When <see cref="Exact"/> is false (default),
    /// this performs a prefix match — any query whose key starts with
    /// these elements will match.
    /// </summary>
    public QueryKey? QueryKey { get; init; }

    /// <summary>
    /// When true, only queries whose key matches exactly (same length
    /// and elements) will pass. Default is false (prefix match).
    /// </summary>
    public bool Exact { get; init; }

    /// <summary>
    /// Filter by observer activity.
    /// </summary>
    public QueryTypeFilter Type { get; init; } = QueryTypeFilter.All;

    /// <summary>
    /// Filter by staleness. True = only stale queries, false = only fresh.
    /// Null (default) = no staleness filter.
    /// </summary>
    public bool? Stale { get; init; }

    /// <summary>
    /// Filter by fetch status (Fetching, Paused, Idle).
    /// Null (default) = no fetch status filter.
    /// </summary>
    public FetchStatus? FetchStatus { get; init; }

    /// <summary>
    /// Arbitrary predicate applied after all other filters.
    /// </summary>
    public Func<Query, bool>? Predicate { get; init; }
}
