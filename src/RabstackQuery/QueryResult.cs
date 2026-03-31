using System.Diagnostics;

namespace RabstackQuery;

/// <summary>
/// A point-in-time snapshot of a query's data, error, and status flags,
/// produced by a <see cref="QueryObserver{TData, TQueryData}"/> for consumption by UI layers.
/// </summary>
public sealed class QueryResult<TData> : IQueryResult<TData>
{
    private readonly QueryState<TData> _state;
    private readonly QueryObserverOptions<TData, TData>? _options;
    private readonly TimeProvider _timeProvider;
    private readonly bool _isPlaceholderData;
    private readonly RefetchDelegate<TData>? _refetch;

    // Snapshot of the query's update counts at observer attach time, used to
    // compute IsFetchedAfterMount. See QueryObserver._currentQueryInitialState.
    private readonly int _initialDataUpdateCount;
    private readonly int _initialErrorUpdateCount;

    public QueryResult(
        QueryState<TData> state,
        QueryObserverOptions<TData, TData>? options = null,
        TimeProvider? timeProvider = null,
        bool isPlaceholderData = false,
        RefetchDelegate<TData>? refetch = null,
        int initialDataUpdateCount = 0,
        int initialErrorUpdateCount = 0)
    {
        // All production call sites pass a TimeProvider from QueryClient. The
        // fallback to TimeProvider.System exists only for backward compatibility
        // but silently bypasses the fake clock in tests — flag it during development.
        Debug.Assert(timeProvider is not null, "TimeProvider should be explicitly provided; falling back to System clock");

        _state = state;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isPlaceholderData = isPlaceholderData;
        _refetch = refetch;
        _initialDataUpdateCount = initialDataUpdateCount;
        _initialErrorUpdateCount = initialErrorUpdateCount;
    }

    public TData? Data => _state.Data;

    public DateTimeOffset DataUpdatedAt => DateTimeOffset.FromUnixTimeMilliseconds(_state.DataUpdatedAt);

    public Exception? Error => _state.Error;

    public DateTimeOffset ErrorUpdatedAt => DateTimeOffset.FromUnixTimeMilliseconds(_state.ErrorUpdatedAt);

    public int FailureCount => _state.FetchFailureCount;

    public Exception? FailureReason => _state.FetchFailureReason;

    public int ErrorUpdateCount => _state.ErrorUpdateCount;

    public bool IsError => _state.Status == QueryStatus.Errored;

    public bool IsFetched => _state.DataUpdateCount > 0 || _state.ErrorUpdateCount > 0;

    /// <summary>
    /// Whether the query has completed at least one fetch since this observer
    /// attached. Compares current update counts against the snapshot captured
    /// when the observer bound to the query. Mirrors TanStack's
    /// <c>isFetchedAfterMount</c> in <c>queryObserver.ts:576–578</c>.
    /// </summary>
    public bool IsFetchedAfterMount =>
        _state.DataUpdateCount > _initialDataUpdateCount
        || _state.ErrorUpdateCount > _initialErrorUpdateCount;

    public bool IsFetching => _state.FetchStatus == FetchStatus.Fetching;

    public bool IsLoading => _state.Status == QueryStatus.Pending && IsFetching;

    public bool IsPending => _state.Status == QueryStatus.Pending;

    public bool IsLoadingError => _state.Status == QueryStatus.Errored && _state.DataUpdateCount == 0;

    public bool IsPaused => _state.FetchStatus == FetchStatus.Paused;

    public bool IsPlaceholderData => _isPlaceholderData;

    public bool IsRefetchError => _state.Status == QueryStatus.Errored && _state.DataUpdateCount > 0;

    public bool IsRefetching => _state.FetchStatus == FetchStatus.Fetching && _state.DataUpdateCount > 0;

    /// <summary>
    /// Whether the data is stale according to the configured <c>StaleTime</c>.
    /// <para>
    /// Note: this property reads the current clock on each access and can
    /// transition from <c>false</c> to <c>true</c> over time without a new
    /// result being created. This is consistent with TanStack Query's dynamic
    /// staleness evaluation.
    /// </para>
    /// </summary>
    public bool IsStale
    {
        get
        {
            if (_options is null) return true;

            // Disabled observers are never considered stale — they don't participate
            // in automatic refetching, so reporting stale would be misleading.
            // Mirrors TanStack's behavior where disabled observers report isStale=false.
            if (!_options.Enabled) return false;

            // No data = always stale, regardless of staleTime.
            // TanStack checks `this.state.data === undefined` without a timestamp
            // condition. In C#, null can be a legitimate data value (unlike TS
            // undefined), so we only treat it as "no data" when the query has never
            // successfully fetched (DataUpdatedAt == 0).
            if (_state.Data is null && _state.DataUpdatedAt == 0) return true;

            return QueryTimeDefaults.IsStale(
                _state.DataUpdatedAt, _options.StaleTime, _state.IsInvalidated, _timeProvider.GetUtcNowMs());
        }
    }

    public bool IsSuccess => _state.Status == QueryStatus.Succeeded;

    public bool IsEnabled => _options?.Enabled ?? true;

    /// <inheritdoc />
    public Task<IQueryResult<TData>> RefetchAsync(
        RefetchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_refetch is null)
        {
            throw new InvalidOperationException(
                "Cannot refetch from a result that is not bound to an observer. " +
                "This result was created without a refetch delegate.");
        }

        return _refetch(options, cancellationToken);
    }

    public QueryStatus Status => _state.Status;

    public FetchStatus FetchStatus => _state.FetchStatus;
}
