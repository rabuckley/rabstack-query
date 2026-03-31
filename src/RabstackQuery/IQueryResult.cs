namespace RabstackQuery;

/// <summary>
/// Snapshot of a query's current state, produced by a <see cref="QueryObserver{TData,TQueryData}"/>.
/// </summary>
/// <remarks>
/// This interface is implemented by the framework. Consumers should not implement it directly.
/// </remarks>
public interface IQueryResult<TData>
{
    /// <summary>
    /// The last successfully resolved data for the query.
    /// </summary>
    TData? Data { get; }

    /// <summary>
    /// The time at which the data was last successfully updated.
    /// </summary>
    DateTimeOffset DataUpdatedAt { get; }

    /// <summary>
    /// The error object for the query, if it failed.
    /// </summary>
    Exception? Error { get; }

    /// <summary>
    /// The time at which the query last failed.
    /// </summary>
    DateTimeOffset ErrorUpdatedAt { get; }

    /// <summary>
    /// The failure count for the query.
    /// <list type="bullet">
    /// <item>Incremented each time the query fails.</item>
    /// <item>Resets to zero when the query succeeds.</item>
    /// </list>
    /// </summary>
    int FailureCount { get; }

    // TODO: Better name?
    /// <summary>
    /// The failure reason for the query retry.
    /// </summary>
    Exception? FailureReason { get; }

    // TODO: Better name?
    /// <summary>
    /// The sum of all errors.
    /// </summary>
    public int ErrorUpdateCount { get; }

    /// <summary>
    /// <see langword="true"/> if the query is in an error state.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// <see langword="true"/> if the query has completed at least one fetch (success or error).
    /// </summary>
    public bool IsFetched { get; }

    /// <summary>
    /// Whether the query has completed at least one fetch (success or error)
    /// since this observer attached. Distinguishes cached-before-mount data from
    /// freshly-fetched data.
    /// <para>
    /// Mirrors TanStack's <c>isFetchedAfterMount</c> which compares current
    /// update counts against a snapshot captured when the observer bound to the
    /// query (<c>queryObserver.ts:576–578</c>).
    /// </para>
    /// </summary>
    public bool IsFetchedAfterMount { get; }

    /// <summary>
    /// <see langword="true"/> if a fetch is currently in flight (including background refetches).
    /// </summary>
    public bool IsFetching { get; }

    /// <summary>
    /// <see langword="true"/> if the query is fetching for the first time with no cached data.
    /// Equivalent to <c>IsPending &amp;&amp; IsFetching</c>.
    /// </summary>
    public bool IsLoading { get; }

    /// <summary>
    /// <see langword="true"/> if the query has no cached data yet (status is <see cref="QueryStatus.Pending"/>).
    /// </summary>
    public bool IsPending { get; }

    /// <summary>
    /// <see langword="true"/> if the query failed while fetching for the first time.
    /// </summary>
    public bool IsLoadingError { get; }

    /// <summary>
    /// <see langword="true"/> if the query's fetch is paused (e.g., waiting for network connectivity).
    /// </summary>
    public bool IsPaused { get; }

    /// <summary>
    /// <see langword="true"/> if the current data came from <c>PlaceholderData</c>
    /// rather than an actual fetch.
    /// </summary>
    public bool IsPlaceholderData { get; }

    /// <summary>
    /// <see langword="true"/> if the query failed during a background refetch (data was already cached).
    /// </summary>
    public bool IsRefetchError { get; }

    /// <summary>
    /// <see langword="true"/> if a background refetch is in progress while cached data exists.
    /// </summary>
    public bool IsRefetching { get; }

    /// <summary>
    /// <see langword="true"/> if the cached data is considered stale according to <c>StaleTime</c>.
    /// </summary>
    public bool IsStale { get; }

    /// <summary>
    /// <see langword="true"/> if the query has successfully fetched data.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// <see langword="true"/> if the query's observer is enabled and will fetch on mount.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Triggers a refetch of this query's data and returns the updated result.
    /// <para>
    /// Mirrors TanStack's <c>refetch</c> function on <c>QueryObserverBaseResult</c>,
    /// bound to the observer that created this result. Allows consumers to trigger
    /// a refetch directly from the result without holding an observer reference.
    /// </para>
    /// </summary>
    Task<IQueryResult<TData>> RefetchAsync(
        RefetchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Zero-argument overload for binary-stable interface evolution.</summary>
    Task<IQueryResult<TData>> RefetchAsync() => RefetchAsync(null, default);

    /// <summary>
    /// The overall query status: <see cref="QueryStatus.Pending"/>,
    /// <see cref="QueryStatus.Error"/>, or <see cref="QueryStatus.Success"/>.
    /// </summary>
    public QueryStatus Status { get; }

    /// <summary>
    /// The current fetch status: <see cref="RabstackQuery.FetchStatus.Fetching"/>,
    /// <see cref="RabstackQuery.FetchStatus.Paused"/>, or <see cref="RabstackQuery.FetchStatus.Idle"/>.
    /// </summary>
    public FetchStatus FetchStatus { get; }
}
