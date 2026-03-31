namespace RabstackQuery;

/// <summary>
/// Result interface for infinite queries. Extends the base query result with
/// page-specific status properties and page navigation methods. Mirrors
/// TanStack's <c>InfiniteQueryObserverBaseResult</c> from
/// <c>infiniteQueryObserver.ts</c>.
/// </summary>
public interface IInfiniteQueryResult<TData, TPageParam> : IQueryResult<InfiniteData<TData, TPageParam>>
{
    /// <summary>
    /// Whether there is a next page available, determined by
    /// <c>GetNextPageParam</c> returning <see cref="PageParamResult{T}.HasValue"/>.
    /// </summary>
    bool HasNextPage { get; }

    /// <summary>
    /// Whether there is a previous page available, determined by
    /// <c>GetPreviousPageParam</c> returning <see cref="PageParamResult{T}.HasValue"/>.
    /// </summary>
    bool HasPreviousPage { get; }

    /// <summary>
    /// Whether the query is currently fetching the next page.
    /// </summary>
    bool IsFetchingNextPage { get; }

    /// <summary>
    /// Whether the query is currently fetching the previous page.
    /// </summary>
    bool IsFetchingPreviousPage { get; }

    /// <summary>
    /// Whether the query errored while fetching the next page.
    /// </summary>
    bool IsFetchNextPageError { get; }

    /// <summary>
    /// Whether the query errored while fetching the previous page.
    /// </summary>
    bool IsFetchPreviousPageError { get; }

    /// <summary>
    /// Fetches the next page. The observer holds a reference to the underlying query
    /// to issue directional fetches — same lifecycle pattern as
    /// <see cref="IQueryResult{TData}.RefetchAsync"/> which also holds an observer reference.
    /// </summary>
    Task FetchNextPageAsync(CancellationToken cancellationToken = default);

    /// <summary>Zero-argument overload for binary-stable interface evolution.</summary>
    Task FetchNextPageAsync() => FetchNextPageAsync(default);

    /// <summary>
    /// Fetches the previous page.
    /// </summary>
    Task FetchPreviousPageAsync(CancellationToken cancellationToken = default);

    /// <summary>Zero-argument overload for binary-stable interface evolution.</summary>
    Task FetchPreviousPageAsync() => FetchPreviousPageAsync(default);
}
