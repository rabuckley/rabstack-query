namespace RabstackQuery;

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

    public bool IsError { get; }

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

    public bool IsFetching { get; }

    public bool IsLoading { get; }

    public bool IsPending { get; }

    /// <summary>
    /// <see langword="true"/> if the query failed while fetching for the first time.
    /// </summary>
    public bool IsLoadingError { get; }

    public bool IsPaused { get; }

    public bool IsPlaceholderData { get; }

    public bool IsRefetchError { get; }

    public bool IsRefetching { get; }

    public bool IsStale { get; }

    public bool IsSuccess { get; }

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

    public QueryStatus Status { get; }

    public FetchStatus FetchStatus { get; }
}
