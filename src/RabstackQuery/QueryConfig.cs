namespace RabstackQuery;

/// <summary>
/// Initialization parameters passed to a <see cref="Query{TData}"/> constructor,
/// bundling the owning client, key, hash, options, and optional pre-existing state.
/// </summary>
public sealed class QueryConfig<TData>
{
    public required QueryClient Client { get; init; }

    public required QueryKey QueryKey { get; init; }

    public required string QueryHash { get; init; }

    public QueryConfiguration<TData>? Options { get; init; }

    public QueryConfiguration<TData>? DefaultOptions { get; init; }

    public QueryState<TData>? State { get; init; }

    /// <summary>
    /// Centralised metrics instruments. Null when metrics are disabled.
    /// Threaded from <see cref="QueryCache"/> which receives it via
    /// <see cref="QueryCache.SetMetrics"/>.
    /// </summary>
    internal QueryMetrics? Metrics { get; init; }

    /// <summary>
    /// Whether this query is a hydrated placeholder that will be upgraded
    /// to a properly-typed query when an observer subscribes.
    /// </summary>
    internal bool IsHydratedPlaceholder { get; init; }
}
