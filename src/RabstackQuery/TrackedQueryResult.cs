namespace RabstackQuery;

/// <summary>
/// Wrapper around <see cref="IQueryResult{TData}"/> that records which properties
/// are accessed by the consumer. Each property getter delegates to the inner result
/// and invokes a callback with the property name on first access.
///
/// <para>
/// C# equivalent of TanStack's JS Proxy-based <c>trackResult</c>
/// (<c>queryObserver.ts:263–291</c>). Since C# has no Proxy, this uses explicit
/// delegation with a tracking callback instead.
/// </para>
/// </summary>
internal sealed class TrackedQueryResult<TData>(
    IQueryResult<TData> inner,
    Action<string> onPropTracked) : IQueryResult<TData>
{
    private void Track(string prop) => onPropTracked(prop);

    public TData? Data { get { Track(QueryResultProps.Data); return inner.Data; } }
    public DateTimeOffset DataUpdatedAt { get { Track(QueryResultProps.DataUpdatedAt); return inner.DataUpdatedAt; } }
    public Exception? Error { get { Track(QueryResultProps.Error); return inner.Error; } }
    public DateTimeOffset ErrorUpdatedAt { get { Track(QueryResultProps.ErrorUpdatedAt); return inner.ErrorUpdatedAt; } }
    public int FailureCount { get { Track(QueryResultProps.FailureCount); return inner.FailureCount; } }
    public Exception? FailureReason { get { Track(QueryResultProps.FailureReason); return inner.FailureReason; } }
    public int ErrorUpdateCount { get { Track(QueryResultProps.ErrorUpdateCount); return inner.ErrorUpdateCount; } }
    public bool IsError { get { Track(QueryResultProps.IsError); return inner.IsError; } }
    public bool IsFetched { get { Track(QueryResultProps.IsFetched); return inner.IsFetched; } }
    public bool IsFetchedAfterMount { get { Track(QueryResultProps.IsFetchedAfterMount); return inner.IsFetchedAfterMount; } }
    public bool IsFetching { get { Track(QueryResultProps.IsFetching); return inner.IsFetching; } }
    public bool IsLoading { get { Track(QueryResultProps.IsLoading); return inner.IsLoading; } }
    public bool IsPending { get { Track(QueryResultProps.IsPending); return inner.IsPending; } }
    public bool IsLoadingError { get { Track(QueryResultProps.IsLoadingError); return inner.IsLoadingError; } }
    public bool IsPaused { get { Track(QueryResultProps.IsPaused); return inner.IsPaused; } }
    public bool IsPlaceholderData { get { Track(QueryResultProps.IsPlaceholderData); return inner.IsPlaceholderData; } }
    public bool IsRefetchError { get { Track(QueryResultProps.IsRefetchError); return inner.IsRefetchError; } }
    public bool IsRefetching { get { Track(QueryResultProps.IsRefetching); return inner.IsRefetching; } }
    public bool IsStale { get { Track(QueryResultProps.IsStale); return inner.IsStale; } }
    public bool IsSuccess { get { Track(QueryResultProps.IsSuccess); return inner.IsSuccess; } }
    public bool IsEnabled { get { Track(QueryResultProps.IsEnabled); return inner.IsEnabled; } }
    public QueryStatus Status { get { Track(QueryResultProps.Status); return inner.Status; } }
    public FetchStatus FetchStatus { get { Track(QueryResultProps.FetchStatus); return inner.FetchStatus; } }

    /// <summary>
    /// Delegates without tracking — RefetchAsync is a method, not an observable
    /// property. TanStack doesn't track it either.
    /// </summary>
    public Task<IQueryResult<TData>> RefetchAsync(
        RefetchOptions? options = null,
        CancellationToken cancellationToken = default)
        => inner.RefetchAsync(options, cancellationToken);
}
