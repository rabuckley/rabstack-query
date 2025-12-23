namespace RabstackQuery;

/// <summary>
/// Internal implementation of <see cref="IInfiniteQueryResult{TData,TPageParam}"/>.
/// Wraps a base <see cref="IQueryResult{TData}"/> (where TData is
/// <see cref="InfiniteData{TData,TPageParam}"/>) and adds computed properties
/// for page navigation status. Mirrors TanStack's result computation in
/// <c>infiniteQueryObserver.ts:152–181</c>.
/// </summary>
internal sealed class InfiniteQueryResult<TData, TPageParam> : IInfiniteQueryResult<TData, TPageParam>
{
    private readonly IQueryResult<InfiniteData<TData, TPageParam>> _inner;
    private readonly InfiniteQueryObserver<TData, TPageParam> _observer;
    private readonly InfiniteData<TData, TPageParam>? _transformedData;
    private readonly bool _hasNextPage;
    private readonly bool _hasPreviousPage;
    private readonly FetchDirection? _lastFetchDirection;

    internal InfiniteQueryResult(
        IQueryResult<InfiniteData<TData, TPageParam>> inner,
        InfiniteQueryObserver<TData, TPageParam> observer,
        InfiniteData<TData, TPageParam>? transformedData,
        bool hasNextPage,
        bool hasPreviousPage,
        FetchDirection? lastFetchDirection)
    {
        _inner = inner;
        _observer = observer;
        _transformedData = transformedData;
        _hasNextPage = hasNextPage;
        _hasPreviousPage = hasPreviousPage;
        _lastFetchDirection = lastFetchDirection;
    }

    // ── Infinite-specific properties ────────────────────────────────────

    public bool HasNextPage => _hasNextPage;

    public bool HasPreviousPage => _hasPreviousPage;

    /// <summary>
    /// True when fetching AND the last fetch direction was forward.
    /// Mirrors TanStack's <c>isFetchingNextPage</c> computation.
    /// </summary>
    public bool IsFetchingNextPage =>
        _inner.IsFetching && _lastFetchDirection is FetchDirection.Forward;

    /// <summary>
    /// True when fetching AND the last fetch direction was backward.
    /// Mirrors TanStack's <c>isFetchingPreviousPage</c> computation.
    /// </summary>
    public bool IsFetchingPreviousPage =>
        _inner.IsFetching && _lastFetchDirection is FetchDirection.Backward;

    public bool IsFetchNextPageError =>
        _inner.IsError && _lastFetchDirection is FetchDirection.Forward;

    public bool IsFetchPreviousPageError =>
        _inner.IsError && _lastFetchDirection is FetchDirection.Backward;

    public Task FetchNextPageAsync(CancellationToken cancellationToken = default)
        => _observer.FetchNextPageAsync(cancellationToken);

    public Task FetchPreviousPageAsync(CancellationToken cancellationToken = default)
        => _observer.FetchPreviousPageAsync(cancellationToken);

    // ── Overridden base properties to exclude page-specific fetches ─────
    // Mirrors infiniteQueryObserver.ts:177–181 which overrides isRefetching
    // and isRefetchError to exclude directional page fetches.

    /// <summary>
    /// A refetch is a background fetch that is NOT a directional page fetch.
    /// Overrides the base computation to exclude forward/backward page fetches.
    /// </summary>
    public bool IsRefetching =>
        _inner.IsFetching && !IsFetchingNextPage && !IsFetchingPreviousPage
        && _inner.Status is not QueryStatus.Pending;

    public bool IsRefetchError =>
        _inner.IsError && !IsFetchNextPageError && !IsFetchPreviousPageError
        && _inner.Data is not null;

    // ── Delegated base properties ───────────────────────────────────────

    public InfiniteData<TData, TPageParam>? Data => _transformedData ?? _inner.Data;
    public DateTimeOffset DataUpdatedAt => _inner.DataUpdatedAt;
    public Exception? Error => _inner.Error;
    public DateTimeOffset ErrorUpdatedAt => _inner.ErrorUpdatedAt;
    public int FailureCount => _inner.FailureCount;
    public Exception? FailureReason => _inner.FailureReason;
    public int ErrorUpdateCount => _inner.ErrorUpdateCount;
    public bool IsError => _inner.IsError;
    public bool IsFetched => _inner.IsFetched;
    public bool IsFetchedAfterMount => _inner.IsFetchedAfterMount;
    public bool IsFetching => _inner.IsFetching;
    public bool IsLoading => _inner.IsLoading;
    public bool IsPending => _inner.IsPending;
    public bool IsLoadingError => _inner.IsLoadingError;
    public bool IsPaused => _inner.IsPaused;
    public bool IsPlaceholderData => _inner.IsPlaceholderData;
    public bool IsStale => _inner.IsStale;
    public bool IsSuccess => _inner.IsSuccess;
    public bool IsEnabled => _inner.IsEnabled;
    public QueryStatus Status => _inner.Status;
    public FetchStatus FetchStatus => _inner.FetchStatus;

    public Task<IQueryResult<InfiniteData<TData, TPageParam>>> RefetchAsync(
        RefetchOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.RefetchAsync(options, cancellationToken);
}
