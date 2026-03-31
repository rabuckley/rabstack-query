using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

/// <summary>
/// Non-generic base class for queries. Allows the <see cref="QueryCache"/> to store
/// queries without knowing their data type parameter.
/// </summary>
/// <remarks>
/// <para>The only concrete subclass is the internal <c>Query&lt;TData&gt;</c>.
/// This class is not designed for subclassing outside of RabstackQuery.</para>
/// <para><b>Threading:</b> query state mutations and observer notifications are not
/// inherently thread-safe. Callers must ensure queries are accessed from a single
/// context (typically the UI/synchronization context).</para>
/// </remarks>
public abstract class Query : Removable
{
    internal Query(TimeProvider timeProvider) : base(timeProvider) { }

    public string? QueryHash { get; protected init; }

    public QueryKey? QueryKey { get; protected init; }

    /// <summary>
    /// Marks the query as invalidated (stale).
    /// </summary>
    public abstract void Invalidate();

    /// <summary>
    /// Fetches fresh data for this query.
    /// </summary>
    public abstract Task Fetch(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches fresh data for this query with an explicit <paramref name="cancelRefetch"/> flag.
    /// When true, an in-flight fetch with existing data is cancelled and a new fetch starts.
    /// When false, the existing in-flight fetch is deduplicated.
    /// </summary>
    internal abstract Task Fetch(bool cancelRefetch, CancellationToken cancellationToken = default);

    /// <summary>Number of observers currently subscribed to this query.</summary>
    public abstract int ObserverCount { get; }

    /// <summary>Current fetch status of this query.</summary>
    public abstract FetchStatus CurrentFetchStatus { get; }

    /// <summary>Current query status (Pending, Succeeded, Errored).</summary>
    public abstract QueryStatus CurrentStatus { get; }

    /// <summary>
    /// Whether this query is stale — either explicitly invalidated or has no data.
    /// </summary>
    public abstract bool IsStale();

    /// <summary>
    /// Whether this query has at least one active (enabled) observer.
    /// Mirrors TanStack's <c>query.isActive()</c>, which checks
    /// <c>observers.some(o => resolveEnabled(o.options.enabled, this) !== false)</c>.
    /// </summary>
    public abstract bool IsActive();

    /// <summary>
    /// Whether this query is disabled — no active observers and either never
    /// fetched data or has no query function. Mirrors TanStack's
    /// <c>query.isDisabled()</c>, used to skip disabled queries during
    /// <c>RefetchQueriesAsync</c>.
    /// </summary>
    public abstract bool IsDisabled();

    /// <summary>
    /// Whether any observer has static stale time (never stale, not even after
    /// invalidation). Mirrors TanStack's <c>query.isStatic()</c>, used to skip
    /// static queries during <c>RefetchQueriesAsync</c>.
    /// </summary>
    public abstract bool IsStatic();

    /// <summary>
    /// Resets the query to its initial state.
    /// </summary>
    public abstract void Reset();

    /// <summary>
    /// Cancels any in-flight fetch for this query.
    /// </summary>
    public abstract Task Cancel(CancelOptions? options);

    /// <summary>Zero-argument overload for binary-stable API evolution.</summary>
    public Task Cancel() => Cancel(null);

    /// <summary>
    /// Called when the app regains window focus. Delegates to observers to decide
    /// whether a refetch is warranted based on their <c>RefetchOnWindowFocus</c> setting.
    /// </summary>
    public abstract void OnFocus();

    /// <summary>
    /// Called when network connectivity is restored. Delegates to observers to decide
    /// whether a refetch is warranted based on their <c>RefetchOnReconnect</c> setting.
    /// </summary>
    public abstract void OnOnline();

    /// <summary>Optional metadata associated with this query.</summary>
    public abstract Meta? Meta { get; }

    /// <summary>
    /// Unix millisecond timestamp of the last successful data update.
    /// Used by hydration to compare freshness when deciding whether to
    /// overwrite existing cached state.
    /// </summary>
    internal abstract long DataUpdatedAt { get; }

    /// <summary>
    /// Whether this query is a hydrated placeholder (<c>Query&lt;object&gt;</c>)
    /// that has not yet been upgraded to a properly-typed query. Placeholders
    /// are created during <see cref="QueryClient.Hydrate"/> and upgraded when
    /// <see cref="QueryCache.GetOrCreate{TData, TQueryData}"/> is called for the same hash.
    /// </summary>
    internal bool IsHydratedPlaceholder { get; private protected init; }

    /// <summary>
    /// Produces a type-erased snapshot of this query's state for serialization.
    /// </summary>
    internal abstract DehydratedQuery Dehydrate(long dehydratedAt);

    /// <summary>
    /// Double-dispatch: passes the typed <c>Query&lt;TData&gt;</c> to the operation,
    /// recovering the generic type that is erased on this non-generic base class.
    /// This is the C# equivalent of TypeScript's structural typing — callers that
    /// hold a <see cref="Query"/> reference can execute typed logic without knowing
    /// <c>TData</c> at the call site.
    /// </summary>
    internal abstract TResult Accept<TResult>(IQueryOperation<TResult> operation);

    /// <summary>
    /// Applies dehydrated state to this query. Used when hydrating into an
    /// existing query that has newer data on the server than in the local cache.
    /// Preserves the current <see cref="FetchStatus"/>.
    /// </summary>
    internal abstract void ApplyDehydratedState(DehydratedQueryState state);
}

public sealed class Query<TData> : Query
{
    private static readonly Func<bool> AlwaysCanRun = static () => true;

    private readonly QueryCache _cache;
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly QueryMetrics? _metrics;
    private QueryState<TData> _initialState;
    private readonly QueryConfiguration<TData>? _defaultOptions;

    // Cached delegates to avoid per-fetch allocations in FetchCore.
    private readonly Action<int, Exception> _onRetryerFail;
    private readonly Action _onRetryerPause;
    private readonly Action _onRetryerContinue;

    private readonly Lock _observerLock = new();
    private readonly List<IQueryObserver> _observers = [];
    private Func<QueryFunctionContext, Task<TData>>? _queryFn;

    internal Func<QueryFunctionContext, Task<TData>>? QueryFn => _queryFn;

    // Tracks whether the query function accessed QueryFunctionContext.CancellationToken
    // during the current fetch. When true, RemoveObserver performs a hard cancel (with
    // state revert) instead of a soft CancelRetry. Mirrors TanStack's
    // #abortSignalConsumed flag (query.ts:177).
    // Kept separate from FetchOperation because it's written mid-flight by the query
    // function callback — bundling would capture a stale reference after an atomic swap.
    private volatile bool _abortSignalConsumed;

    // Bundles the active retryer and pre-fetch state snapshot into a single volatile
    // reference. Superseding a fetch becomes one atomic swap instead of coordinating
    // two separate volatile fields. The identity check `_activeFetch?.Retryer == retryer`
    // replaces the previous `_retryer == retryer` pattern.
    private volatile FetchOperation? _activeFetch;

    // Fetch deduplication: when a fetch is in-flight, subsequent Fetch() calls
    // return this same Task instead of starting a new fetch. This mirrors
    // TanStack's pattern of returning `this.#retryer.promise` (query.ts:404).
    // Kept separate from FetchOperation because the task IS FetchCore — creating
    // the bundle inside FetchCore would require a self-reference to the task.
    private volatile Task? _currentFetchTask;

    private sealed class FetchOperation
    {
        public required Retryer<TData> Retryer { get; init; }
        public required QueryState<TData> PreFetchState { get; init; }
    }

    internal void AddObserver(IQueryObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_observerLock)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);

                // TanStack query.ts:347-350 — cancel any pending GC timer when
                // an observer attaches so the query isn't removed from the cache
                // while actively observed.
                ClearGcTimeout();
            }
        }

        // TanStack query.ts:350 — notify outside the lock (listeners may re-enter).
        _cache.Notify(new QueryCacheObserverAddedEvent { Query = this, Observer = observer });
    }

    internal void RemoveObserver(IQueryObserver observer)
    {
        lock (_observerLock)
        {
            if (!_observers.Remove(observer))
            {
                return;
            }

            // TanStack query.ts:358–369 — when the last observer leaves, decide
            // how to handle the in-flight fetch based on whether the query function
            // consumed the cancellation token:
            //   - Consumed: hard cancel with state revert (the function cooperates
            //     with cancellation, so stopping it is safe and expected).
            //   - Not consumed: soft CancelRetry (stop future retries but let the
            //     current attempt finish so the result can be cached).
            if (_observers.Count == 0)
            {
                if (_abortSignalConsumed)
                {
                    Cancel(new CancelOptions { Revert = true });
                }
                else
                {
                    _activeFetch?.Retryer.CancelRetry();
                }

                ScheduleGc();
            }
        }
    }

    public Query(QueryConfig<TData> config) : base(config.Client.TimeProvider)
    {
        _defaultOptions = config.DefaultOptions;
        _client = config.Client;
        _logger = _client.LoggerFactory.CreateLogger<Query<TData>>();
        _metrics = config.Metrics;
        _cache = _client.QueryCache;
        QueryKey = config.QueryKey;
        QueryHash = config.QueryHash;
        IsHydratedPlaceholder = config.IsHydratedPlaceholder;

        _onRetryerFail = (count, error) => Dispatch(new FailedAction { FailureCount = count, Error = error });
        _onRetryerPause = () => Dispatch(new PauseAction());
        _onRetryerContinue = () => Dispatch(new ContinueAction());

        SetOptions(config.Options);
        _initialState = GetDefaultState(Options!);
        State = config.State ?? _initialState;
        ScheduleGc();
    }

    public QueryConfiguration<TData> Options { get; private set; } = null!;

    // Volatile: State is written by Dispatch (any thread via Retryer callbacks) and
    // read from event threads (OnFocus, OnOnline), timer callbacks (OptionalRemove),
    // and caller threads (IsStale, FetchInternal dedup). Without volatile, the JIT
    // may cache reads in registers, causing cross-thread reads to see stale values.
    private volatile QueryState<TData>? _state;
    public QueryState<TData>? State { get => _state; private set => _state = value; }

    public override Meta? Meta => Options?.Meta;

    internal override long DataUpdatedAt => State?.DataUpdatedAt ?? 0;

    public override int ObserverCount { get { lock (_observerLock) return _observers.Count; } }

    public override FetchStatus CurrentFetchStatus => State?.FetchStatus ?? FetchStatus.Idle;

    public override QueryStatus CurrentStatus => State?.Status ?? QueryStatus.Pending;

    public override bool IsStale()
    {
        // Check observers first — their IsStale incorporates StaleTime, static,
        // and enabled state via IsStaleByTime. Mirrors TanStack's query.isStale():
        // `if (this.getObserversCount() > 0) return this.observers.some(
        //     (observer) => observer.getCurrentResult().isStale)`
        lock (_observerLock)
        {
            if (_observers.Count > 0)
            {
                foreach (var o in _observers)
                {
                    if (o.IsCurrentResultStale)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // No observers: fall back to simple state checks.
        // Mirrors TanStack's `return this.state.data === undefined || this.state.isInvalidated`.
        if (State is null)
        {
            return true;
        }

        if (State.Data is null)
        {
            return true;
        }

        return State.IsInvalidated;
    }

    public override bool IsActive()
    {
        // Mirrors TanStack's `this.observers.some(o => resolveEnabled(o.options.enabled, this) !== false)`.
        // Manual loop avoids the LINQ `.Any()` allocation (enumerator boxing + delegate)
        // on this hot path called from QueryKeyMatcher during bulk operations.
        lock (_observerLock)
        {
            return IsActiveCore();
        }
    }

    public override bool IsDisabled()
    {
        lock (_observerLock)
        {
            if (_observers.Count > 0)
            {
                return !IsActiveCore();
            }
        }

        // No observers: disabled if there's no query function or the query has
        // never attempted a fetch (created via SetQueryData only).
        return _queryFn is null
            || (State is not null && State.DataUpdateCount + State.ErrorUpdateCount == 0);
    }

    /// <summary>
    /// Caller must hold <see cref="_observerLock"/>. Mirrors TanStack's
    /// <c>observers.some(o => resolveEnabled(o.options.enabled, this) !== false)</c>.
    /// Extracted so <see cref="IsDisabled"/> can reuse without reentrant locking.
    /// </summary>
    private bool IsActiveCore()
    {
        foreach (var o in _observers)
        {
            if (o.IsEnabled)
            {
                return true;
            }
        }

        return false;
    }

    public override bool IsStatic()
    {
        // Mirrors TanStack's `query.isStatic()`: returns true if any observer
        // has a resolved staleTime of 'static'. Queries with no observers are
        // not considered static.
        lock (_observerLock)
        {
            if (_observers.Count == 0)
            {
                return false;
            }

            foreach (var o in _observers)
            {
                if (o.IsStaleTimeStatic)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public override void Reset()
    {
        _logger.QueryReset(QueryHash);

        // TanStack calls destroy() then setState(#initialState). destroy() clears
        // the GC timer and cancels any in-flight fetch.
        Destroy();
        var op = _activeFetch;
        op?.Retryer.Cancel();
        _activeFetch = null;
        _currentFetchTask = null;

        // Recalculate initial state from Options. This reinvokes InitialDataFactory
        // (if set) to get fresh data, and uses the current clock for timestamps.
        // For queries created via SetQueryData (which don't set InitialData on
        // Options), this produces Status=Pending with Data=null — the correct
        // reset target.
        _initialState = GetDefaultState(Options);
        Dispatch(new SetStateAction<TData> { State = _initialState, Options = null });

        // Reschedule GC so the reset query is still eligible for garbage collection.
        ScheduleGc();
    }

    public override Task Cancel(CancelOptions? options)
    {
        _logger.QueryCancelled(QueryHash, options?.Revert ?? false);
        var op = _activeFetch;

        // Dispatch state revert BEFORE cancelling the retryer.
        //
        // C# divergence: In TanStack, retryer.cancel() rejects the promise
        // (discarding the query function's return value) and fires onCancel
        // which reverts state — all synchronously. In C#, StreamedQuery
        // catches OperationCanceledException and returns partial data as a
        // success, so the Retryer resolves and FetchCore dispatches
        // SuccessState. Because retryer.Cancel() can trigger inline async
        // continuations (no SynchronizationContext in many C# hosts), the
        // entire FetchCore success path can execute during this Cancel()
        // call. Dispatching the revert first ensures the success overwrites
        // the revert (correct ordering), not the reverse.
        if (options?.Revert is true && op is not null)
        {
            Dispatch(new SetStateAction<TData> { State = op.PreFetchState, Options = null });
        }

        op?.Retryer.Cancel();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds the first observer that wants to refetch on window focus and triggers
    /// a fetch. Only one observer needs to say "yes" since data is shared at the
    /// query level. Mirrors TanStack's <c>query.onFocus()</c> pattern.
    /// </summary>
    public override void OnFocus()
    {
        // Capture the fetch decision under the lock; perform I/O outside to
        // keep lock scope minimal and avoid holding it during FetchSilentAsync.
        bool shouldFetch;
        lock (_observerLock)
        {
            shouldFetch = false;
            foreach (var observer in _observers)
            {
                if (observer.ShouldFetchOnWindowFocus())
                {
                    shouldFetch = true;
                    break;
                }
            }
        }

        if (shouldFetch)
        {
            _logger.QueryOnFocusRefetch(QueryHash);
            _metrics?.RefetchOnFocusTotal?.Add(1);

            // Only one observer needs to say "yes" since data is shared at the
            // query level. We call Fetch() directly rather than observer.Refetch()
            // — the Dispatch path notifies all observers regardless.
            // FetchSilentAsync passes cancelRefetch: false, matching TanStack's
            // observer.refetch({ cancelRefetch: false }) (query.ts:328).
            _ = FetchSilentAsync();
        }

        // TanStack query.ts:331 — resume paused retryer when focus returns.
        _activeFetch?.Retryer.Continue();
    }

    /// <summary>
    /// Finds the first observer that wants to refetch on reconnect and triggers
    /// a fetch. Mirrors TanStack's <c>query.onOnline()</c> pattern.
    /// </summary>
    public override void OnOnline()
    {
        bool shouldFetch;
        lock (_observerLock)
        {
            shouldFetch = false;
            foreach (var observer in _observers)
            {
                if (observer.ShouldFetchOnReconnect())
                {
                    shouldFetch = true;
                    break;
                }
            }
        }

        if (shouldFetch)
        {
            _logger.QueryOnOnlineRefetch(QueryHash);
            _metrics?.RefetchOnReconnectTotal?.Add(1);
            _ = FetchSilentAsync();
        }

        // TanStack query.ts:340 — resume paused retryer when connectivity returns.
        _activeFetch?.Retryer.Continue();
    }

    /// <summary>
    /// Fire-and-forget fetch that swallows all exceptions. Used by
    /// <see cref="OnFocus"/> and <see cref="OnOnline"/> where errors are
    /// silently ignored — same as TanStack's <c>.catch(noop)</c> pattern.
    /// Unlike <c>Task.Run</c>, this runs the synchronous preamble of
    /// <see cref="Fetch"/> on the current thread, avoiding thread pool
    /// scheduling delays. Passes <c>cancelRefetch: false</c> to match
    /// TanStack's <c>observer.refetch({ cancelRefetch: false })</c>
    /// (<c>query.ts:328</c>).
    /// </summary>
    private async Task FetchSilentAsync()
    {
        try { await Fetch(cancelRefetch: false); }
        catch (Exception) { /* swallow — background refetch errors are silent */ }
    }

    private QueryState<TData> GetDefaultState(QueryConfiguration<TData> options)
    {
        // TanStack query.ts:729–732 — resolve initialData if it's a function.
        // HasInitialData tracks whether InitialData was explicitly assigned,
        // which matters for value types where default(TData) (e.g. 0 for int)
        // is a valid value and indistinguishable from "not set" via null checks.
        var hasExplicitData = options.InitialDataFactory is not null || options.HasInitialData;
        var data = options.InitialDataFactory is not null
            ? options.InitialDataFactory()
            : options.HasInitialData ? options.InitialData : default;

        // TanStack: typeof initialData !== 'undefined' && initialData !== null
        // → success. In C#, we use the explicit flag plus a null check to handle
        // both reference types (where null means "no data") and value types.
        var hasData = hasExplicitData && data is not null;

        // TanStack query.ts:736–740 — resolve initialDataUpdatedAt, falling
        // back to the current time when data is present but no timestamp given
        long dataUpdatedAt;
        if (!hasData)
        {
            dataUpdatedAt = 0;
        }
        else
        {
            var explicitTimestamp = options.InitialDataUpdatedAtFactory is not null
                ? options.InitialDataUpdatedAtFactory()
                : options.InitialDataUpdatedAt;
            dataUpdatedAt = explicitTimestamp ?? GetUtcNowMs();
        }

        return new QueryState<TData>
        {
            Data = data,
            DataUpdateCount = hasData ? 1 : 0,
            DataUpdatedAt = dataUpdatedAt,
            Error = null,
            ErrorUpdateCount = 0,
            ErrorUpdatedAt = 0,
            FetchFailureCount = 0,
            FetchFailureReason = null,
            FetchMeta = null,
            IsInvalidated = false,
            Status = hasData ? QueryStatus.Succeeded : QueryStatus.Pending,
            FetchStatus = FetchStatus.Idle
        };
    }

    private void SetOptions(QueryConfiguration<TData>? configOptions)
    {
        Options = configOptions ?? new QueryConfiguration<TData> { GcTime = QueryTimeDefaults.GcTime };
        UpdateGcTime(Options.GcTime);

        if (this.State is not null && this.State.Data is null)
        {
            var defaultState = GetDefaultState(Options);

            if (defaultState.Data is not null)
            {
                SetState(SuccessState(defaultState.Data, defaultState.DataUpdatedAt));
            }
            _initialState = defaultState;
        }
    }

    internal void SetState(QueryState<TData> state, SetStateOptions? options = null)
    {
        Dispatch(new SetStateAction<TData> { State = state, Options = options });
    }

    internal void SetQueryFn(Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        _queryFn = queryFn;
    }

    public override Task Fetch(CancellationToken cancellationToken = default)
        => FetchInternal(meta: null, cancelRefetch: false, cancellationToken);

    /// <summary>
    /// Fetches with an explicit <paramref name="cancelRefetch"/> flag. Used by
    /// <see cref="QueryClient.RefetchQueriesAsync(QueryFilters?, RefetchOptions?, CancellationToken)"/>
    /// to thread through the caller's <see cref="RefetchOptions.CancelRefetch"/> setting.
    /// </summary>
    internal override Task Fetch(bool cancelRefetch, CancellationToken cancellationToken = default)
        => FetchInternal(meta: null, cancelRefetch, cancellationToken);

    /// <summary>
    /// Fetches with directional metadata for infinite queries. The FetchMeta
    /// is written to State via Dispatch(FetchAction) so the query function can
    /// read State.FetchMeta.FetchMore.Direction at execution time.
    /// </summary>
    internal Task Fetch(FetchMeta meta, CancellationToken cancellationToken = default)
        => FetchInternal(meta, cancelRefetch: false, cancellationToken);

    private Task FetchInternal(FetchMeta? meta, bool cancelRefetch, CancellationToken cancellationToken)
    {
        // Deduplication: if a fetch is already in-flight and the retryer hasn't
        // been rejected/cancelled, decide whether to cancel it (cancelRefetch=true)
        // or deduplicate (cancelRefetch=false). Mirrors TanStack query.ts:390–405.
        // Capture volatile fields to locals to prevent torn reads — another thread
        // may null these between the null check and the dereference.
        var op = _activeFetch;
        var fetchTask = _currentFetchTask;
        var state = State;
        if (state is not null
            && state.FetchStatus is not FetchStatus.Idle
            && op is not null
            && !op.Retryer.IsCancelled
            && fetchTask is not null)
        {
            // TanStack query.ts:397–405 — when the query already has data and
            // cancelRefetch is true, cancel the in-flight fetch silently and fall
            // through to start a new one. Otherwise, deduplicate by returning the
            // existing task.
            if (state.Data is not null && cancelRefetch)
            {
                // C# concurrency guard: TanStack is single-threaded, so the
                // old fetch's handlers run in microtask order and never race
                // with the new fetch. In C#, the old FetchCore's completion
                // can run on a thread pool thread concurrently with the new
                // FetchCore. If the old one dispatches SuccessState (e.g.,
                // StreamedQuery catches OperationCanceledException and returns
                // partial data), it overwrites the new fetch's state.
                //
                // Null _activeFetch BEFORE cancelling so the old FetchCore can
                // detect it's been superseded via the identity check, even if
                // its completion runs inline during retryer.Cancel().
                // For non-refetch cancels (e.g., unsubscribe), _activeFetch is
                // NOT nulled, so partial data is preserved — matching TanStack.
                _logger.QueryCancelled(QueryHash, false);
                _activeFetch = null;
                op.Retryer.Cancel();
                // Fall through to start new fetch
            }
            else
            {
                // TanStack query.ts:400–404 — when deduplicating, undo any
                // cancelRetry from a previous RemoveObserver so the in-flight
                // fetch can continue retrying.
                op.Retryer.ContinueRetry();

                _logger.QueryFetchDeduplicated(QueryHash);
                _metrics?.QueryFetchDeduplicatedTotal?.Add(1, QueryMetrics.QueryHashTag(QueryHash));
                return fetchTask;
            }
        }

        _logger.QueryFetchStarted(QueryHash);
        _currentFetchTask = FetchCore(meta, cancellationToken);
        return _currentFetchTask;
    }

    private async Task FetchCore(FetchMeta? meta, CancellationToken cancellationToken)
    {
        if (_queryFn is null)
        {
            throw new InvalidOperationException("Query function is not set");
        }

        // Capture state before fetch so Cancel(revert: true) can restore it
        var preFetchState = State!;

        // Reset the signal-consumed flag for this fetch. The flag is set when
        // the query function accesses QueryFunctionContext.CancellationToken.
        // Mirrors TanStack's `this.#abortSignalConsumed = false` (query.ts:443).
        _abortSignalConsumed = false;

        // Set fetching state (with optional FetchMeta for infinite query direction)
        Dispatch(new FetchAction { Meta = meta });

        // Capture _queryFn to a local — another thread could null it between the
        // null check at the top of FetchCore and the lambda capture below.
        var queryFn = _queryFn!;

        // Create retryer with exponential backoff and network mode support.
        // Mirrors TanStack query.ts:516–543.
        // The Fn wrapper creates a QueryFunctionContext that tracks whether the
        // query function accesses the CancellationToken — the C# equivalent of
        // TanStack's Object.defineProperty getter trap on context.signal.
        var retryerOptions = new RetryerOptions<TData>
        {
            Fn = ct =>
            {
                var ctx = new QueryFunctionContext(ct, () => _abortSignalConsumed = true, _client, QueryKey!);
                return queryFn(ctx);
            },
            MaxRetries = Options.Retry is > 0 and var r ? r + 1 : 1, // MaxRetries includes initial attempt
            RetryDelay = Options.RetryDelay,
            TimeProvider = _client.TimeProvider,
            LoggerFactory = _client.LoggerFactory,
            Metrics = _metrics,
            RetrySource = "query",
            NetworkMode = Options.NetworkMode,
            CanRun = AlwaysCanRun, // Queries always allow running; mutations use this for scope coordination
            OnlineManager = _client.OnlineManager,
            FocusManager = _client.FocusManager,
            OnFail = _onRetryerFail,
            OnPause = _onRetryerPause,
            OnContinue = _onRetryerContinue
        };

        var retryer = new Retryer<TData>(retryerOptions);

        // ORDERING CONSTRAINT: State must be written (via Dispatch above) before
        // this volatile write. FetchInternal's dedup path reads _activeFetch with
        // acquire semantics, which pairs with this release to guarantee visibility
        // of the State write (FetchStatus = Fetching). If these writes were
        // reordered, the dedup check could see a non-null _activeFetch but stale
        // State.FetchStatus == Idle, incorrectly starting a duplicate fetch.
        _activeFetch = new FetchOperation { Retryer = retryer, PreFetchState = preFetchState };

        // Link external cancellation to retryer
        using var registration = cancellationToken.Register(() => retryer.Cancel());

        var queryHashTag = QueryMetrics.QueryHashTag(QueryHash);
        _metrics?.QueryFetchTotal?.Add(1, queryHashTag);

        // Only start the stopwatch when the histogram instrument exists —
        // Stopwatch is a struct so there's no allocation, but the perf counter
        // read (~20ns) is worth skipping when nobody will consume the result.
        var sw = _metrics?.QueryFetchDuration is not null
            ? Stopwatch.StartNew()
            : null;

        try
        {
            var data = await retryer.ExecuteAsync();

            // C# concurrency guard (see FetchInternal comment for full
            // context): StreamedQuery catches OperationCanceledException and
            // returns partial data, so ExecuteAsync can succeed even when the
            // retryer was cancelled. If a refetch superseded this fetch,
            // FetchInternal nulled _retryer before cancelling — so _retryer
            // no longer matches our local. Skip the success dispatch to
            // prevent stale data from overwriting the new fetch's state.
            // For non-refetch cancels (e.g., unsubscribe), _retryer still
            // references this retryer, so partial data is dispatched normally
            // — matching TanStack's behavior.
            if (retryer.IsCancelled && _activeFetch?.Retryer != retryer)
            {
                _metrics?.QueryFetchCancelledTotal?.Add(1, queryHashTag);
                _logger.QueryFetchCancelled(QueryHash);
                return;
            }

            sw?.Stop();
            _metrics?.QueryFetchSuccessTotal?.Add(1, queryHashTag);
            _metrics?.QueryFetchDuration?.Record(sw!.Elapsed.TotalSeconds, queryHashTag);

            var now = GetUtcNowMs();
            SetState(SuccessState(data, now));
            _logger.QueryFetchSucceeded(QueryHash);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error — don't dispatch ErrorAction.
            // State revert is handled by Cancel() if requested.
            // Duration is intentionally not recorded for cancelled fetches.
            _metrics?.QueryFetchCancelledTotal?.Add(1, queryHashTag);
            _logger.QueryFetchCancelled(QueryHash);
            throw;
        }
        catch (Exception ex)
        {
            sw?.Stop();
            _metrics?.QueryFetchErrorTotal?.Add(1, queryHashTag);
            _metrics?.QueryFetchDuration?.Record(sw!.Elapsed.TotalSeconds, queryHashTag);

            _logger.QueryFetchFailed(QueryHash, State?.FetchFailureCount ?? 0, ex);
            Dispatch(new ErrorAction { Error = ex });
            throw;
        }
        finally
        {
            retryer.Dispose();

            // Only clear shared fields if they still reference THIS fetch's
            // operation. A refetch replaces _activeFetch before starting the
            // new FetchCore, so a mismatch means another fetch has taken over —
            // blindly nulling would clobber the new fetch's references.
            if (_activeFetch?.Retryer == retryer)
            {
                _activeFetch = null;
                _currentFetchTask = null;
            }

            // If all observers left during this fetch (via RemoveObserver →
            // CancelRetry), the GC timer from RemoveObserver may have already
            // fired and found FetchStatus != Idle. Now that the fetch is
            // complete (Idle), reschedule so the query can be collected.
            int observerCount;
            lock (_observerLock) { observerCount = _observers.Count; }
            if (observerCount == 0)
            {
                ScheduleGc();
            }
        }
    }

    private static QueryState<TData> SuccessState(TData data, long dataUpdatedAt)
    {
        return new QueryState<TData>
        {
            Data = data,
            DataUpdateCount = 1,
            DataUpdatedAt = dataUpdatedAt,
            Error = null,
            ErrorUpdateCount = 0,
            ErrorUpdatedAt = 0,
            FetchFailureCount = 0,
            FetchFailureReason = null,
            FetchMeta = null,
            IsInvalidated = false,
            Status = QueryStatus.Succeeded,
            FetchStatus = FetchStatus.Idle
        };
    }

    /// <summary>
    /// Marks the query as invalidated, which will trigger a refetch on next access if observers are active.
    /// </summary>
    public override void Invalidate()
    {
        if (State is not null)
        {
            _logger.QueryInvalidated(QueryHash);
            _metrics?.InvalidationTotal?.Add(1);
            Dispatch(new InvalidateAction());
        }
    }

    internal override TResult Accept<TResult>(IQueryOperation<TResult> operation)
        => operation.Execute(this);

    internal override DehydratedQuery Dehydrate(long dehydratedAt)
    {
        Debug.Assert(State is not null);
        return new DehydratedQuery
        {
            QueryHash = QueryHash!,
            QueryKey = QueryKey!,
            State = new DehydratedQueryState
            {
                Data = State.Data,
                DataUpdateCount = State.DataUpdateCount,
                DataUpdatedAt = State.DataUpdatedAt,
                Error = State.Error,
                ErrorUpdateCount = State.ErrorUpdateCount,
                ErrorUpdatedAt = State.ErrorUpdatedAt,
                FetchFailureCount = State.FetchFailureCount,
                FetchFailureReason = State.FetchFailureReason,
                FetchMeta = State.FetchMeta,
                IsInvalidated = State.IsInvalidated,
                Status = State.Status,
                FetchStatus = State.FetchStatus,
            },
            Meta = Meta,
            DehydratedAt = dehydratedAt,
        };
    }

    internal override void ApplyDehydratedState(DehydratedQueryState state)
    {
        // TanStack overwrites the full state when dehydrated data is newer.
        // Uses dehydrated DataUpdateCount directly (no increment).
        // Preserves current FetchStatus so in-flight fetches are not disrupted.
        var newState = new QueryState<TData>
        {
            Data = state.Data is TData typed ? typed : default,
            DataUpdateCount = state.DataUpdateCount,
            DataUpdatedAt = state.DataUpdatedAt,
            Error = state.Error,
            ErrorUpdateCount = state.ErrorUpdateCount,
            ErrorUpdatedAt = state.ErrorUpdatedAt,
            FetchFailureCount = state.FetchFailureCount,
            FetchFailureReason = state.FetchFailureReason,
            FetchMeta = state.FetchMeta,
            IsInvalidated = state.IsInvalidated,
            Status = state.Status,
            FetchStatus = State?.FetchStatus ?? FetchStatus.Idle,
        };

        SetState(newState);
    }

    protected override void OptionalRemove()
    {
        int observerCount;
        lock (_observerLock) { observerCount = _observers.Count; }
        if (observerCount == 0 && State is not null && State.FetchStatus == FetchStatus.Idle)
        {
            _logger.QueryGcRemoved(QueryHash);
            _metrics?.CacheGcRemovedTotal?.Add(1);
            _cache.Remove(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var op = _activeFetch;
            _activeFetch = null;
            op?.Retryer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Dispatch(DispatchAction action)
    {
        _logger.QueryDispatch(QueryHash, action.GetType().Name);

        QueryState<TData> Reducer(QueryState<TData> state)
        {
            return action switch
            {
                SetStateAction<TData> setStateAction => setStateAction.State,

                // TanStack query.ts:628–633 + fetchState():690–708 — set initial
                // fetch status based on network availability.
                FetchAction fetchAction => state with
                {
                    FetchMeta = fetchAction.Meta,
                    IsInvalidated = false,
                    FetchStatus = NetworkModeHelper.CanFetch(Options.NetworkMode, _client.OnlineManager)
                        ? FetchStatus.Fetching
                        : FetchStatus.Paused
                },

                ErrorAction errorAction => state with
                {
                    Error = errorAction.Error,
                    ErrorUpdateCount = state.ErrorUpdateCount + 1,
                    ErrorUpdatedAt = GetUtcNowMs(),
                    FetchFailureCount = state.FetchFailureCount + 1,
                    FetchFailureReason = errorAction.Error,
                    // Flag existing data as invalidated on background error so the
                    // query is considered stale. "No data" is always stale anyway, so
                    // setting unconditionally is correct. Matches TanStack query.ts.
                    IsInvalidated = true,
                    Status = QueryStatus.Errored,
                    FetchStatus = FetchStatus.Idle
                },

                FailedAction failedAction => state with
                {
                    Error = failedAction.Error,
                    FetchFailureCount = failedAction.FailureCount,
                    FetchFailureReason = failedAction.Error,
                    FetchStatus = FetchStatus.Fetching // Keep fetching while retrying
                },

                InvalidateAction => state with { IsInvalidated = true },

                // TanStack query.ts:620–622 — retryer entered pause (offline/unfocused).
                PauseAction => state with { FetchStatus = FetchStatus.Paused },

                // TanStack query.ts:623–627 — retryer resumed from pause.
                ContinueAction => state with { FetchStatus = FetchStatus.Fetching },

                _ => throw new InvalidOperationException($"Unknown action type: {action.GetType().Name}"),
            };
        }

        State = Reducer(State!);

        _client.NotifyManager.Batch(() =>
        {
            // Snapshot the observer list under the lock, then iterate outside it.
            // This prevents holding the lock during OnQueryUpdate callbacks, which
            // could otherwise deadlock or corrupt _observers if a callback path
            // re-enters AddObserver/RemoveObserver on the same thread.
            IQueryObserver[] snapshot;
            lock (_observerLock)
            {
                snapshot = _observers.ToArray();
            }

            foreach (var observer in snapshot)
            {
                observer.OnQueryUpdate(action);
            }

            _cache.Notify(new QueryCacheQueryUpdatedEvent { Query = this, Action = action });
        });
    }
}
