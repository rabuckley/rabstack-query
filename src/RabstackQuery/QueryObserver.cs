using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Convenience subclass for the common case where the cached type and the returned
/// type are the same (no Select transform). Allows writing
/// <c>new QueryObserverOptions&lt;string&gt;</c> instead of
/// <c>new QueryObserverOptions&lt;string, string&gt;</c>, and enables the
/// single-type-parameter <c>UseQuery&lt;TData&gt;</c> extension overload without
/// generic ambiguity.
/// </summary>
public record QueryObserverOptions<TData> : QueryObserverOptions<TData, TData>;

/// <summary>
/// Convenience subclass for the common case where the cached type and the returned
/// type are the same (no Select transform). Allows writing
/// <c>new QueryObserver&lt;string&gt;(client, options)</c> instead of
/// <c>new QueryObserver&lt;string, string&gt;(client, options)</c>.
/// </summary>
public sealed class QueryObserver<TData> : QueryObserver<TData, TData>
{
    public QueryObserver(QueryClient client, QueryObserverOptions<TData> options)
        : base(client, options)
    {
    }
}

/// <summary>
/// Subscribes to a <see cref="Query{TData}"/> and produces <see cref="QueryResult{TData}"/>
/// snapshots, handling stale-time evaluation, refetch scheduling, and placeholder data.
/// </summary>
public class QueryObserver<TData, TQueryData> : Subscribable<QueryObserverListener<TData>>, IQueryObserver
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private QueryObserverOptions<TData, TQueryData> _options;

    private ResolvedQuerySettings _settings;
    private Query<TQueryData>? _currentQuery;
    private IQueryResult<TData>? _currentResult;
    private ITimer? _refetchIntervalTimer;

    // Subscription callbacks hold the listeners lock before taking this lock. Read
    // _isMounted here instead of HasListeners/ListenerCount to avoid reversing that order.
    private readonly Lock _refetchIntervalLock = new();

    // Guarded by _refetchIntervalLock.
    private bool _isMounted;
    private ITimer? _staleTimeoutTimer;
    private TimeSpan _currentRefetchInterval;

    // Memoization state bundled into an immutable snapshot. Mirrors TanStack's
    // `#currentResultOptions`, `#currentResultState`, and `#lastQueryWithDefinedData`
    // fields in queryObserver.ts. Bundling prevents the class of bug at line 478
    // of the original code where scattered fields could be read in an inconsistent
    // state during UpdateResult.
    private MemoSnapshot _memo = new();

    private sealed class MemoSnapshot
    {
        public QueryObserverOptions<TData, TQueryData>? Options { get; init; }
        public QueryState<TQueryData>? State { get; init; }
        public Query<TQueryData>? LastQueryWithDefinedData { get; init; }
    }

    // Snapshot of the query's update counts at the time this observer attached,
    // used to compute IsFetchedAfterMount. Mirrors TanStack's
    // `#currentQueryInitialState` in queryObserver.ts.
    private QueryState<TQueryData>? _currentQueryInitialState;

    public QueryObserver(
        QueryClient client,
        QueryObserverOptions<TData, TQueryData> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.QueryObserver");
        _options = options;
        _settings = client.ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults());
        UpdateQuery();
    }

    public bool IsEnabled => ResolveEnabled();

    public bool IsStaleTimeStatic => ResolveStaleTime() == Timeout.InfiniteTimeSpan;

    public bool IsCurrentResultStale => CurrentResult.IsStale;

    /// <summary>
    /// The current options this observer was last configured with.
    /// Used by <see cref="QueriesObserver{TData}"/> to read the query key for
    /// hash-based observer reuse.
    /// </summary>
    public QueryObserverOptions<TData, TQueryData> Options => _options;

    /// <summary>
    /// The underlying query this observer is attached to. Used by
    /// <see cref="InfiniteQueryObserver{TData,TPageParam}"/> to call
    /// <see cref="Query{TData}.Fetch(FetchMeta, CancellationToken)"/>
    /// for directional page fetches.
    /// </summary>
    internal Query<TQueryData>? CurrentQuery => _currentQuery;

    public void SetOptions(QueryObserverOptions<TData, TQueryData> options)
    {
        var prevOptions = _options;
        var prevEnabled = ResolveEnabled();
        _options = options;
        _settings = _client.ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults());

        // If query key changed, update the query
        if (!QueryKeyEquals(prevOptions.QueryKey, options.QueryKey))
        {
            var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
            var oldHash = hasher.HashQueryKey(prevOptions.QueryKey);
            var newHash = hasher.HashQueryKey(options.QueryKey);
            _logger.ObserverKeyChanged(oldHash, newHash);

            // Unregister from old query
            _currentQuery?.RemoveObserver(this);

            // Register with new query
            UpdateQuery();

            if (ListenerCount > 0 && ShouldFetchOptionally(queryChanged: true, prevEnabled))
            {
                _ = ExecuteFetch();
            }
        }
        else
        {
            // Key unchanged — update the query function if a real one is provided
            // (skip the sentinel so Query.IsDisabled() stays correct).
            if (_options.QueryFn is not null
                && !SkipToken.IsSkipToken(_options.QueryFn)
                && _currentQuery is not null)
            {
                _currentQuery.SetQueryFn(_options.QueryFn);
            }

            // Recompute the result to apply option changes (e.g.
            // a new Select transform, StaleTime, or Enabled flag). Mirrors TanStack's
            // setOptions which always calls updateResult(). The memoization inside
            // UpdateResult() keeps the same _currentResult reference when nothing
            // meaningful changed, preserving QueriesObserver's change detection.
            UpdateResult();

            if (ListenerCount > 0 && ShouldFetchOptionally(queryChanged: false, prevEnabled))
            {
                _ = ExecuteFetch();
            }
        }

        // Re-evaluate the polling timer. TanStack computes the interval from the
        // function (if any) and compares the resolved value against the cached one,
        // so a function that returns the same duration won't needlessly restart the
        // timer. We also restart when enabled or background flags change.
        var nextInterval = ComputeRefetchInterval();
        var currentEnabled = ResolveEnabled();
        if (nextInterval != _currentRefetchInterval
            || prevEnabled != currentEnabled
            || prevOptions.RefetchIntervalInBackground != options.RefetchIntervalInBackground)
        {
            UpdateRefetchInterval(nextInterval);
        }

        // TanStack queryObserver.ts fires observerOptionsUpdated on every setOptions
        // call so cache listeners can react to option changes (e.g. for DevTools).
        if (_currentQuery is not null)
        {
            _client.QueryCache.Notify(new QueryCacheObserverOptionsUpdatedEvent
            {
                Query = _currentQuery,
                Observer = this
            });
        }
    }

    private void UpdateQuery()
    {
        var queryCache = _client.QueryCache;

        // Deliberately omit InitialData — the observer creates a bare query that
        // starts as Pending. Setting InitialData (even to default) would mark the
        // query as Succeeded, which is wrong for value types like int where
        // default(int) = 0 is indistinguishable from "has data = 0".
        var queryOptions = new QueryConfiguration<TQueryData>
        {
            QueryKey = _options.QueryKey,
            QueryKeyHasher = _options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance,
            Meta = _options.Meta,
        };
        _settings.CopyTo(queryOptions);

        _currentQuery = queryCache.GetOrCreate<TQueryData, TQueryData>(_client, queryOptions);

        // Set query function if provided — but never install the skipToken
        // sentinel onto the query. Without this guard, the throwing sentinel
        // would be set as _queryFn, and Query.IsDisabled() would see non-null
        // _queryFn and incorrectly report enabled for unobserved queries.
        if (_options.QueryFn is not null && !SkipToken.IsSkipToken(_options.QueryFn))
        {
            _currentQuery.SetQueryFn(_options.QueryFn);
        }

        // Snapshot the query's current state before we attach. This captures
        // DataUpdateCount and ErrorUpdateCount so IsFetchedAfterMount can
        // detect fetches that complete after this observer attached. Mirrors
        // TanStack's `this.#currentQueryInitialState = query.state` in
        // queryObserver.ts #updateQuery().
        _currentQueryInitialState = _currentQuery.State;

        // TanStack queryObserver.ts:712-715 — only register with the query when
        // there are listeners. In the constructor path (no listeners yet), we skip
        // AddObserver; OnSubscribe will add it when the first listener arrives.
        if (HasListeners)
        {
            _currentQuery.AddObserver(this);
        }

        UpdateResult();
    }

    public void OnQueryUpdate(DispatchAction action)
    {
        UpdateResult();

        NotifyListeners();

        // Re-evaluate timers after every state change. This is critical for the
        // RefetchIntervalFn form: the function may return a different interval
        // based on updated query state (e.g., slow down after N successful fetches).
        // Mirrors TanStack's onQueryUpdate which calls #updateTimers().
        if (HasListeners)
        {
            UpdateTimers();
        }
    }

    private void UpdateResult()
    {
        if (_currentQuery?.State is null)
        {
            _currentResult = CreateInitialResult();
            return;
        }

        var state = _currentQuery.State;
        var prevResult = _currentResult;
        var memo = _memo;
        var prevOptions = memo.Options;

        // Placeholder data activation: the query must be pending with no data,
        // and a PlaceholderData delegate must be configured.
        var shouldActivatePlaceholder =
            _options.PlaceholderData is not null
            && state.Data is null
            && state.Status is QueryStatus.Pending;

        TQueryData? data = state.Data;
        QueryStatus status = state.Status;
        bool isPlaceholderData = false;

        if (shouldActivatePlaceholder && _options.PlaceholderData is { } placeholderDataFn)
        {
            // Memoization path: if the previous result was already placeholder data
            // AND the PlaceholderData delegate reference hasn't changed, reuse the
            // previous data directly. This avoids redundant Select invocations when
            // the observer is stable. Memoization requires callers to reuse the same
            // delegate instance — natural for method groups and captured variables.
            if (prevResult is { IsPlaceholderData: true }
                && prevOptions?.PlaceholderData is not null
                && ReferenceEquals(placeholderDataFn, prevOptions.PlaceholderData))
            {
                // Reuse previous result wholesale — data and Select output are unchanged
                _memo = new MemoSnapshot
                {
                    Options = _options,
                    State = state,
                    LastQueryWithDefinedData = memo.LastQueryWithDefinedData,
                };
                return;
            }

            // Invoke the placeholder function. Pass the last query that had real
            // (non-placeholder) data so KeepPreviousData can propagate it.
            // Avoid `?.State?.Data` because `TQueryData?` is not valid for
            // unconstrained generic types — extract the data manually.
            var previousData = memo.LastQueryWithDefinedData?.State is { } prevState
                ? prevState.Data
                : default;
            var placeholderResult = placeholderDataFn(
                previousData,
                memo.LastQueryWithDefinedData);

            if (placeholderResult is not null)
            {
                data = placeholderResult;
                status = QueryStatus.Succeeded;
                isPlaceholderData = true;
            }
        }

        // Apply Select transform. The placeholder memoization path above returns
        // early, so if we reach here we always need to consider Select.
        TData? transformedData = default;
        if (_options.Select is not null && data is not null)
        {
            // Memoize: skip Select when the raw query data reference hasn't changed
            // and the Select function is the same. This avoids redundant selector
            // invocations during state transitions that don't affect data (e.g.,
            // FetchAction dispatch preserves existing data while only changing
            // FetchStatus). Mirrors TanStack's selectResult memoization.
            if (!isPlaceholderData
                && memo.State is not null
                && ReferenceEquals(data, memo.State.Data)
                && prevOptions?.Select is not null
                && ReferenceEquals(_options.Select, prevOptions.Select)
                && _currentResult is not null)
            {
                transformedData = _currentResult.Data;
            }
            else
            {
                transformedData = _options.Select(data);
                // TanStack queryObserver.ts:537 — structural sharing on Select output
                if (_currentResult is { Data: not null } && transformedData is not null)
                {
                    transformedData = ApplyStructuralSharing(
                        _currentResult.Data, transformedData, _options.StructuralSharing);
                }
            }
        }
        else if (typeof(TData) == typeof(TQueryData))
        {
            transformedData = (TData?)(object?)data;
            // Structural sharing on raw data when no Select is configured
            if (_currentResult is { Data: not null } && transformedData is not null)
            {
                transformedData = ApplyStructuralSharing(
                    _currentResult.Data, transformedData, _options.StructuralSharing);
            }
        }

        // Memoization: if the computed result is structurally identical to the
        // current result, keep the existing _currentResult reference. This preserves
        // reference equality for QueriesObserver's change detection (which uses
        // ReferenceEquals to skip spurious notifications when SetQueries is called
        // with the same query list). Mirrors TanStack's shallowEqualObjects check
        // in QueriesObserver.setQueries.
        if (IsResultUnchanged(memo, state, status, transformedData, isPlaceholderData))
        {
            _memo = new MemoSnapshot
            {
                Options = _options,
                State = state,
                LastQueryWithDefinedData = (!isPlaceholderData && state.Data is not null)
                    ? _currentQuery
                    : memo.LastQueryWithDefinedData,
            };
            return;
        }

        var transformedState = new QueryState<TData>
        {
            Data = transformedData,
            DataUpdateCount = state.DataUpdateCount,
            DataUpdatedAt = state.DataUpdatedAt,
            Error = state.Error,
            ErrorUpdateCount = state.ErrorUpdateCount,
            ErrorUpdatedAt = state.ErrorUpdatedAt,
            FetchFailureCount = state.FetchFailureCount,
            FetchFailureReason = state.FetchFailureReason,
            FetchMeta = state.FetchMeta,
            IsInvalidated = state.IsInvalidated,
            Status = status,
            FetchStatus = state.FetchStatus
        };

        _currentResult = new QueryResult<TData>(
            transformedState, ResolveEnabled(), ResolveStaleTime(), _client.TimeProvider, isPlaceholderData,
            refetch: RefetchAsync,
            initialDataUpdateCount: _currentQueryInitialState?.DataUpdateCount ?? 0,
            initialErrorUpdateCount: _currentQueryInitialState?.ErrorUpdateCount ?? 0);

        // Update memoization bookkeeping atomically
        _memo = new MemoSnapshot
        {
            Options = _options,
            State = state,
            LastQueryWithDefinedData = (!isPlaceholderData && state.Data is not null)
                ? _currentQuery
                : memo.LastQueryWithDefinedData,
        };
    }

    /// <summary>
    /// Memoization gate: returns <see langword="true"/> when the computed result
    /// is structurally identical to <see cref="_currentResult"/>, so the caller
    /// can skip building a new <see cref="QueryResult{TData}"/>. This preserves
    /// reference equality for <see cref="QueriesObserver{TData}"/>'s change
    /// detection. Mirrors TanStack's <c>shallowEqualObjects</c> check.
    /// </summary>
    private bool IsResultUnchanged(
        MemoSnapshot memo,
        QueryState<TQueryData> state,
        QueryStatus status,
        TData? transformedData,
        bool isPlaceholderData)
    {
        if (_currentResult is null)
            return false;

        // Check whether the previous snapshot's IsInvalidated matches the new
        // state. Without this, InvalidateAction dispatches are invisible to the
        // memoization gate because none of the other compared fields change —
        // so _currentResult keeps the old IsInvalidated=false snapshot and
        // IsStale returns the wrong value.
        var prevIsInvalidated = memo.State?.IsInvalidated ?? false;

        var isFetchedAfterMount =
            state.DataUpdateCount > (_currentQueryInitialState?.DataUpdateCount ?? 0)
            || state.ErrorUpdateCount > (_currentQueryInitialState?.ErrorUpdateCount ?? 0);

        return _currentResult.Status == status
            && _currentResult.FetchStatus == state.FetchStatus
            && _currentResult.IsEnabled == ResolveEnabled()
            && prevIsInvalidated == state.IsInvalidated
            && EqualityComparer<TData?>.Default.Equals(transformedData, _currentResult.Data)
            && ReferenceEquals(state.Error, _currentResult.Error)
            && _currentResult.DataUpdatedAt == DateTimeOffset.FromUnixTimeMilliseconds(state.DataUpdatedAt)
            && _currentResult.FailureCount == state.FetchFailureCount
            && _currentResult.IsPlaceholderData == isPlaceholderData
            && _currentResult.IsFetchedAfterMount == isFetchedAfterMount;
    }

    private void NotifyListeners()
    {
        if (_currentResult is null) return;

        var snapshot = GetListenerSnapshot();
        _client.NotifyManager.Batch(() =>
        {
            foreach (var listener in snapshot)
            {
                listener(_currentResult);
            }
        });
    }

    public IQueryResult<TData> CurrentResult => _currentResult ?? CreateInitialResult();

    private IQueryResult<TData> CreateInitialResult()
    {
        var initialState = new QueryState<TData>
        {
            Data = default,
            DataUpdateCount = 0,
            DataUpdatedAt = 0,
            Error = null,
            ErrorUpdateCount = 0,
            ErrorUpdatedAt = 0,
            FetchFailureCount = 0,
            FetchFailureReason = null,
            FetchMeta = null,
            IsInvalidated = false,
            Status = QueryStatus.Pending,
            FetchStatus = FetchStatus.Idle
        };

        return new QueryResult<TData>(initialState, ResolveEnabled(), ResolveStaleTime(), _client.TimeProvider,
            refetch: RefetchAsync,
            initialDataUpdateCount: _currentQueryInitialState?.DataUpdateCount ?? 0,
            initialErrorUpdateCount: _currentQueryInitialState?.ErrorUpdateCount ?? 0);
    }

    /// <summary>
    /// Internal fetch that suppresses non-cancellation errors, matching
    /// TanStack's <c>#executeFetch</c> which does <c>promise.catch(noop)</c>.
    /// Errors are already captured in the query state via <c>ErrorAction</c>
    /// dispatch, so suppression is safe.
    /// </summary>
    private async Task ExecuteFetch(bool cancelRefetch = false, CancellationToken cancellationToken = default)
    {
        if (_currentQuery is null) return;

        try
        {
            await _currentQuery.Fetch(cancelRefetch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Errors are already captured in the query state via ErrorAction
            // dispatch. Suppress here, matching TanStack's `promise.catch(noop)`
            // in `#executeFetch`.
        }
    }

    public async Task<IQueryResult<TData>> RefetchAsync(
        RefetchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Thread CancelRefetch through to the query's FetchInternal.
        // Defaults to true when called by user code (matching TanStack's
        // `cancelRefetch ?? true` in observer.fetch(), queryObserver.ts:237).
        var cancelRefetch = options?.CancelRefetch ?? true;

        if (_currentQuery is not null)
        {
            if (options?.ThrowOnError is true)
            {
                await _currentQuery.Fetch(cancelRefetch, cancellationToken);
            }
            else
            {
                await ExecuteFetch(cancelRefetch, cancellationToken);
            }
        }

        // Explicit UpdateResult after the fetch completes. When the observer has
        // listeners, Dispatch inside Fetch already triggers OnQueryUpdate →
        // UpdateResult on each state change. But when called without listeners
        // (e.g., Enabled=false, no Subscribe), the observer isn't registered on
        // the query, so OnQueryUpdate never fires. This ensures _currentResult
        // reflects the latest query state regardless of subscription status.
        // Mirrors TanStack's protected fetch() which calls updateResult() after
        // the fetch promise resolves (queryObserver.ts:329-331).
        UpdateResult();

        return CurrentResult;
    }

    protected override void OnSubscribe()
    {
        base.OnSubscribe();

        if (ListenerCount == 1)
        {
            _client.Metrics.ActiveObservers?.Add(1);
            _logger.ObserverSubscribed(_currentQuery?.QueryHash, ListenerCount);

            // Re-add observer to the query (OnUnsubscribe removes it when all
            // listeners leave). AddObserver is idempotent so this is safe on the
            // first subscribe too.
            _currentQuery?.AddObserver(this);

            lock (_refetchIntervalLock) { _isMounted = true; }

            var shouldFetch = ShouldFetchOnMount();
            _logger.ObserverFetchOnMount(_currentQuery?.QueryHash, shouldFetch);

            if (shouldFetch)
            {
                _ = ExecuteFetch();
            }
            else
            {
                UpdateResult();
            }

            UpdateTimers();
        }
    }

    /// <summary>
    /// Determines whether a fetch should be triggered when a listener subscribes.
    /// Split into initial load (<see cref="ShouldLoadOnMount"/>) vs refetch-on-mount.
    /// Mirrors TanStack's <c>shouldFetchOnMount</c> (<c>queryObserver.ts:755-763</c>).
    /// </summary>
    private bool ShouldFetchOnMount()
    {
        // Data exists when the query has been fetched at least once (DataUpdatedAt > 0).
        // We can't use `Data is not null` directly because TQueryData is unconstrained.
        var hasData = _currentQuery?.State is { } state && state.DataUpdatedAt > 0;

        return ShouldLoadOnMount() || (hasData && ShouldFetchOn(_settings.RefetchOnMount));
    }

    /// <summary>
    /// Determines whether this is the initial load (no data exists yet).
    /// When no data has ever been fetched, we always fetch regardless of
    /// <see cref="QueryObserverOptions{TData,TQueryData}.RefetchOnMount"/>.
    /// Mirrors TanStack's <c>shouldLoadOnMount</c> (<c>queryObserver.ts:744-752</c>).
    /// </summary>
    private bool ShouldLoadOnMount()
    {
        if (!ResolveEnabled()) return false;
        if (_currentQuery?.State is null) return true;

        var state = _currentQuery.State;

        // No data yet — always fetch. TanStack checks `data === undefined`;
        // in C# we use DataUpdatedAt == 0 as the "never fetched" sentinel because
        // value-type TQueryData (e.g. int) defaults to a non-null value (0) and
        // `Data is null` would always be false, preventing the initial load.
        // TODO: TanStack also checks !(status === 'error' && retryOnMount === false).
        // RetryOnMount is not yet implemented.
        return state.DataUpdatedAt == 0;
    }

    // TanStack's queryObserver.ts shouldFetchOptionally checks suspense errors.
    // Omit that guard because this library has no suspense mode.
    private bool ShouldFetchOptionally(bool queryChanged, bool prevEnabled)
    {
        if (!queryChanged && prevEnabled) return false;
        if (!ResolveEnabled() || _currentQuery?.State is not { } state) return false;

        return state.DataUpdatedAt == 0 || IsStaleByTime(state, ResolveStaleTime());
    }

    private bool IsStaleByTime(QueryState<TQueryData> state, TimeSpan staleTime)
        => QueryTimeDefaults.IsStale(state.DataUpdatedAt, staleTime, state.IsInvalidated, _client.TimeProvider.GetUtcNowMs());

    public bool ShouldFetchOnWindowFocus() =>
        ShouldFetchOn(_settings.RefetchOnWindowFocus);

    public bool ShouldFetchOnReconnect() =>
        ShouldFetchOn(_settings.RefetchOnReconnect);

    /// <summary>
    /// Shared predicate for focus/reconnect refetch decisions. Mirrors TanStack's
    /// <c>shouldFetchOn(query, options, field)</c> from <c>queryObserver.ts</c>.
    /// </summary>
    private bool ShouldFetchOn(RefetchOnBehavior behavior)
    {
        if (!ResolveEnabled()) return false;

        // Resolve once to avoid invoking the user's StaleTimeFn delegate twice.
        var staleTime = ResolveStaleTime();

        // Static staleTime = never refetch on focus/reconnect, regardless of
        // RefetchOnBehavior. Mirrors TanStack's guard:
        // `resolveStaleTime(options.staleTime, query) !== 'static'`
        if (staleTime == Timeout.InfiniteTimeSpan) return false;

        if (behavior is RefetchOnBehavior.Never) return false;
        if (behavior is RefetchOnBehavior.Always) return true;

        // WhenStale: only refetch if there's existing data that has gone stale.
        // A null state means the query was never fetched — nothing to *re*fetch.
        if (_currentQuery?.State is null) return false;
        return IsStaleByTime(_currentQuery.State, staleTime);
    }

    /// <summary>
    /// Resolves the effective enabled state from either
    /// <see cref="QueryObserverOptions{TData,TQueryData}.EnabledFn"/> (dynamic) or
    /// <see cref="QueryObserverOptions{TData,TQueryData}.Enabled"/> (static).
    /// Mirrors TanStack's <c>resolveEnabled()</c> utility.
    /// </summary>
    private bool ResolveEnabled()
    {
        // Check before EnabledFn so a callback cannot enable a SkipToken query.
        if (SkipToken.IsSkipToken(_options.QueryFn))
            return false;

        if (_options.EnabledFn is { } fn && _currentQuery is not null)
        {
            return fn(_currentQuery);
        }

        return _options.Enabled;
    }

    private TimeSpan ResolveStaleTime()
    {
        if (_options.StaleTimeFn is { } fn && _currentQuery is not null)
        {
            return fn(_currentQuery);
        }

        return _settings.StaleTime;
    }

    /// <summary>
    /// Resolves the effective refetch interval from either <see cref="QueryObserverOptions{TData,TQueryData}.RefetchIntervalFn"/>
    /// (dynamic) or <see cref="QueryObserverOptions{TData,TQueryData}.RefetchInterval"/> (static).
    /// Returns <see cref="TimeSpan.Zero"/> when polling should be disabled, consistent
    /// with the static <see cref="QueryObserverOptions{TData,TQueryData}.RefetchInterval"/> convention.
    /// Mirrors TanStack's <c>#computeRefetchInterval()</c>.
    /// </summary>
    private TimeSpan ComputeRefetchInterval()
    {
        if (_options.RefetchIntervalFn is { } fn && _currentQuery is not null)
        {
            return fn(_currentQuery);
        }

        return _options.RefetchInterval;
    }

    /// <summary>
    /// Re-evaluates all time-dependent behaviors. Called after every state change
    /// (from <see cref="OnQueryUpdate"/>) and on subscribe. Mirrors TanStack's
    /// <c>#updateTimers()</c> (<c>queryObserver.ts:410-413</c>).
    /// </summary>
    private void UpdateTimers()
    {
        UpdateStaleTimeout();
        UpdateRefetchInterval(ComputeRefetchInterval());
    }

    /// <summary>
    /// Sets a one-shot timer that fires when cached data transitions from fresh
    /// to stale, triggering an <see cref="UpdateResult"/> so listeners are notified
    /// of the <see cref="IQueryResult{TData}.IsStale"/> change. Mirrors TanStack's
    /// <c>#updateStaleTimeout()</c> (<c>queryObserver.ts:354-376</c>).
    /// </summary>
    private void UpdateStaleTimeout()
    {
        ClearStaleTimeout();

        var staleTime = ResolveStaleTime();

        // Already stale, or staleTime is not a valid finite timeout — nothing to schedule.
        if (_currentResult is null || _currentResult.IsStale
            || staleTime == TimeSpan.Zero
            || staleTime == Timeout.InfiniteTimeSpan
            || staleTime < TimeSpan.Zero)
        {
            return;
        }

        // Compute time remaining until data becomes stale.
        // Mirrors TanStack's `timeUntilStale(this.#currentResult.dataUpdatedAt, staleTime)`.
        var dataUpdatedAtMs = _currentQuery?.State?.DataUpdatedAt ?? 0;
        if (dataUpdatedAtMs == 0) return;

        var now = _client.TimeProvider.GetUtcNowMs();
        var staleMs = (long)staleTime.TotalMilliseconds;

        // Guard against arithmetic overflow when StaleTime is very large (e.g.,
        // TimeSpan.MaxValue). In that case the data won't become stale in any
        // practical timeframe, so skip scheduling.
        if (staleMs > long.MaxValue - dataUpdatedAtMs)
        {
            return;
        }

        var timeUntilStale = Math.Max(dataUpdatedAtMs + staleMs - now, 0);

        // TanStack adds 1ms because "the timeout is sometimes triggered 1 ms
        // before the stale time expiration" (queryObserver.ts:367-369).
        // Guard: if the +1 would exceed the OS timer API limit, skip
        // scheduling — the timer duration is effectively infinite.
        const long maxTimerMs = 4294967294L; // uint.MaxValue - 1: OS timer limit (~49.7 days)
        if (timeUntilStale >= maxTimerMs)
        {
            return;
        }

        var timeout = TimeSpan.FromMilliseconds(timeUntilStale + 1);

        // When the timer fires, IsStale (computed dynamically from the clock)
        // already returns true. Unlike TanStack — where isStale is a snapshot
        // baked into the result object — our IsStale is live. So UpdateResult()
        // would be memoized away (no structural field changed). Instead, notify
        // listeners directly so they re-read the now-stale result.
        _staleTimeoutTimer = _client.TimeProvider.CreateTimer(_ =>
        {
            try
            {
                NotifyListeners();
            }
            catch
            {
                // Swallow — timer callbacks must not throw.
            }
        }, null, timeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Cancels any pending stale-transition timer. Mirrors TanStack's
    /// <c>#clearStaleTimeout()</c> (<c>queryObserver.ts:415-419</c>).
    /// </summary>
    private void ClearStaleTimeout()
    {
        if (_staleTimeoutTimer is not null)
        {
            _staleTimeoutTimer.Dispose();
            _staleTimeoutTimer = null;
        }
    }

    /// <summary>
    /// Creates or recreates the polling timer based on the resolved interval.
    /// Called from <see cref="UpdateTimers"/> or <see cref="SetOptions"/> when
    /// the computed interval changes.
    /// </summary>
    private void UpdateRefetchInterval(TimeSpan nextInterval)
    {
        lock (_refetchIntervalLock)
        {
            ClearRefetchInterval();

            _currentRefetchInterval = nextInterval;

            if (!_isMounted || !ResolveEnabled() || nextInterval <= TimeSpan.Zero) return;

            StartRefetchInterval(nextInterval);
        }
    }

    private void StartRefetchInterval(TimeSpan nextInterval)
    {
        _logger.ObserverRefetchIntervalStarted(nextInterval, _currentQuery?.QueryHash);

        _refetchIntervalTimer = _client.TimeProvider.CreateTimer(_ =>
        {
            try
            {
                // Skip if the app is unfocused and RefetchIntervalInBackground is false
                if (!_options.RefetchIntervalInBackground && !_client.FocusManager.IsFocused)
                {
                    return;
                }

                _ = ExecuteFetch();
            }
            catch
            {
                // Swallow — mimic Removable.cs pattern. Timer callbacks must not
                // throw, as unhandled exceptions in timer callbacks crash the process.
            }
        }, null, nextInterval, nextInterval);
    }

    private void ClearRefetchInterval()
    {
        if (_refetchIntervalTimer is not null)
        {
            _logger.ObserverRefetchIntervalCleared(_currentQuery?.QueryHash);
            _refetchIntervalTimer.Dispose();
            _refetchIntervalTimer = null;
        }
    }

    /// <summary>
    /// Tears down polling and removes this observer from its query.
    /// Mirrors TanStack's <c>destroy()</c>.
    /// </summary>
    private void Destroy()
    {
        ClearStaleTimeout();
        lock (_refetchIntervalLock)
        {
            _isMounted = false;
            ClearRefetchInterval();
        }

        _currentQuery?.RemoveObserver(this);
    }

    protected override void OnUnsubscribe()
    {
        base.OnUnsubscribe();

        _logger.ObserverUnsubscribed(_currentQuery?.QueryHash, ListenerCount);

        // When all listeners unsubscribe, tear down the observer
        if (ListenerCount == 0)
        {
            _client.Metrics.ActiveObservers?.Add(-1);
            Destroy();
        }
    }

    private static TData ApplyStructuralSharing(
        TData prevData, TData newData,
        Func<TData, TData, TData>? structuralSharing)
    {
        if (structuralSharing is not null)
            return structuralSharing(prevData, newData);
        return newData;
    }

    private bool QueryKeyEquals(QueryKey a, QueryKey b)
    {
        var hasher = _options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        return hasher.HashQueryKey(a) == hasher.HashQueryKey(b);
    }
}
