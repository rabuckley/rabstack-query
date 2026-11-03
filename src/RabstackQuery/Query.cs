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
    /// Per-element encodings of <see cref="QueryKey"/>, computed on first use so filters
    /// don't re-encode this key for every invalidation, refetch or removal.
    /// </summary>
    /// <remarks>
    /// Concurrent first reads may each encode the key; the results are equal, so whichever
    /// assignment wins is correct.
    /// </remarks>
    internal string[]? EncodedKeyElements =>
        QueryKey is null ? null : field ??= QueryKeyEncoder.EncodeElements(QueryKey);

    /// <summary>
    /// Marks the query as invalidated (stale).
    /// </summary>
    public abstract void Invalidate();

    /// <summary>
    /// Fetches fresh data for this query.
    /// </summary>
    public abstract Task Fetch();

    /// <summary>
    /// Fetches fresh data for this query with an explicit <paramref name="cancelRefetch"/> flag.
    /// When true, an in-flight fetch with existing data is cancelled and a new fetch starts.
    /// When false, the existing in-flight fetch is deduplicated.
    /// </summary>
    internal abstract Task Fetch(bool cancelRefetch);

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
    /// <param name="options">The cancellation settings; <see langword="null"/> does not revert query state.</param>
    /// <remarks>
    /// The returned task completes once the fetch has settled and never faults. It does not wait for a
    /// query function that ignores its token. That function's eventual result is discarded.
    /// </remarks>
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

    // Cached delegates to avoid per-fetch allocations in FetchCore.
    private readonly Action<int, Exception> _onRetryerFail;
    private readonly Action _onRetryerPause;
    private readonly Action _onRetryerContinue;

    private readonly Lock _observerLock = new();
    private readonly List<IQueryObserver> _observers = [];
    private Func<QueryFunctionContext, Task<TData>>? _queryFn;

    internal Func<QueryFunctionContext, Task<TData>>? QueryFn => _queryFn;

    // Unlike query.ts Query#fetch's single-threaded signal tracking, this flag is
    // written by the query function across threads. Keep it outside FetchOperation
    // so the callback cannot retain an operation replaced by an atomic swap.
    private volatile bool _abortSignalConsumed;

    private volatile FetchOperation? _activeFetch;

    // Keep this on the query so an observer notified of fetch startup can update the
    // revert state before FetchOperation is published.
    private volatile QueryState<TData>? _revertState;

    // This task runs FetchCore, so bundling it inside FetchCore's FetchOperation
    // would require a self-reference.
    private volatile Task? _currentFetchTask;

    private sealed class FetchOperation
    {
        public required Retryer<TData> Retryer { get; init; }

        // Set before cancelling so inline completion sees the first cancellation's options.
        public CancelOptions? Cancellation;

        // FetchCore's task is unavailable during its synchronous preamble and can fault.
        // Cancel needs a non-faulting completion signal available to the query function.
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Cancellation can resume FetchCore before its replacement exists. Waiting for it here
        // avoids making the cancelled fetch await itself through _currentFetchTask.
        public TaskCompletionSource<Task>? Replacement;
    }

    private static readonly CancelOptions PlainCancel = new();
    private static readonly CancelOptions SilentCancel = new() { Silent = true };

    private static void CancelOperation(FetchOperation operation, CancelOptions options)
    {
        Interlocked.CompareExchange(ref operation.Cancellation, options, null);
        operation.Retryer.Cancel();
    }

    internal void AddObserver(IQueryObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_observerLock)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);

                ClearGcTimeout();
            }
        }

        // Notify outside the lock because listeners may re-enter.
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

        Options = config.Options ?? _client.DefaultQueryOptions(new QueryConfiguration<TData> { QueryKey = QueryKey });
        UpdateGcTime(Options.GcTime);
        _initialState = GetDefaultState(Options);
        State = config.State ?? _initialState;
        ScheduleGc();
    }

    // Observers can replace options on other threads; publish each configuration as one reference.
    private volatile QueryConfiguration<TData> _options = null!;

    /// <summary>
    /// Gets the query's cache-level options. Fetches read these values when they start.
    /// </summary>
    /// <remarks>
    /// The last observer or fetch to supply options replaces <see cref="QueryConfiguration{TData}.GcTime"/>,
    /// <see cref="QueryConfiguration{TData}.Retry"/>, <see cref="QueryConfiguration{TData}.RetryDelay"/>,
    /// <see cref="QueryConfiguration{TData}.NetworkMode"/> and <see cref="QueryConfiguration{TData}.Meta"/>.
    /// </remarks>
    public QueryConfiguration<TData> Options { get => _options; private set => _options = value; }

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
        if (_activeFetch is { } op) CancelOperation(op, SilentCancel);
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

        // Revert before cancelling. Cancellation can resume FetchCore inline, and it reads the
        // reverted state to decide whether the fetch resolves or throws.
        if (options?.Revert is true && op is not null && _revertState is { } revertState)
        {
            Dispatch(new SetStateAction<TData>
            {
                State = revertState with { FetchStatus = FetchStatus.Idle },
                Options = null
            });
        }

        if (op is null) return Task.CompletedTask;

        CancelOperation(op, options ?? PlainCancel);
        return op.Settled.Task;
    }

    public override void OnFocus()
    {
        IQueryObserver? refetcher = null;
        lock (_observerLock)
        {
            foreach (var observer in _observers)
            {
                if (observer.ShouldFetchOnWindowFocus())
                {
                    refetcher = observer;
                    break;
                }
            }
        }

        if (refetcher is not null)
        {
            _logger.QueryOnFocusRefetch(QueryHash);
            _metrics?.RefetchOnFocusTotal?.Add(1);
            _ = refetcher.RefetchInBackgroundAsync();
        }

        _activeFetch?.Retryer.Continue();
    }

    public override void OnOnline()
    {
        IQueryObserver? refetcher = null;
        lock (_observerLock)
        {
            foreach (var observer in _observers)
            {
                if (observer.ShouldFetchOnReconnect())
                {
                    refetcher = observer;
                    break;
                }
            }
        }

        if (refetcher is not null)
        {
            _logger.QueryOnOnlineRefetch(QueryHash);
            _metrics?.RefetchOnReconnectTotal?.Add(1);
            _ = refetcher.RefetchInBackgroundAsync();
        }

        _activeFetch?.Retryer.Continue();
    }

    private QueryState<TData> GetDefaultState(QueryConfiguration<TData> options)
    {
        // Unlike query.ts getDefaultState's undefined check, C# needs HasInitialData
        // to distinguish an unset value from valid default(TData), such as zero.
        var hasExplicitData = options.InitialDataFactory is not null || options.HasInitialData;
        var data = options.InitialDataFactory is not null
            ? options.InitialDataFactory()
            : options.HasInitialData ? options.InitialData : default;

        // TanStack: typeof initialData !== 'undefined' && initialData !== null
        // → success. In C#, we use the explicit flag plus a null check to handle
        // both reference types (where null means "no data") and value types.
        var hasData = hasExplicitData && data is not null;

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
            DataUpdateCount = 0,
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

    // TanStack keeps a separate #initialState. Reset() here recomputes it from Options,
    // so option updates must preserve the key and initial-data fields from creation.
    internal void SetOptions(QueryConfiguration<TData> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var current = Options;
        var next = new QueryConfiguration<TData>
        {
            QueryKey = current.QueryKey,
            QueryHash = current.QueryHash,
            QueryKeyHasher = current.QueryKeyHasher,
            InitialDataFactory = current.InitialDataFactory,
            InitialDataUpdatedAt = current.InitialDataUpdatedAt,
            InitialDataUpdatedAtFactory = current.InitialDataUpdatedAtFactory,
            GcTime = options.GcTime,
            Retry = options.Retry,
            RetryDelay = options.RetryDelay,
            NetworkMode = options.NetworkMode,
            Meta = options.Meta,
        };

        if (current.HasInitialData)
        {
            next.InitialData = current.InitialData;
        }

        Options = next;
        UpdateGcTime(next.GcTime);
    }

    internal void SetState(QueryState<TData> state, SetStateOptions? options = null)
    {
        Dispatch(new SetStateAction<TData> { State = state, Options = options });
    }

    internal void SeedInitialData(TData data, long dataUpdatedAt)
    {
        Debug.Assert(State is not null);
        SetState(State with
        {
            Data = data,
            DataUpdatedAt = dataUpdatedAt,
            Error = null,
            IsInvalidated = false,
            Status = QueryStatus.Succeeded,
        });
    }

    internal void SetManualData(TData data, long dataUpdatedAt)
    {
        Dispatch(new SuccessAction<TData> { Data = data, DataUpdatedAt = dataUpdatedAt, Manual = true });
    }

    internal void SetQueryFn(Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        _queryFn = queryFn;
    }

    public override Task Fetch()
        => FetchInternal(meta: null, cancelRefetch: false);

    internal override Task Fetch(bool cancelRefetch)
        => FetchInternal(meta: null, cancelRefetch);

    internal Task Fetch(QueryConfiguration<TData> options, FetchMeta? meta, bool cancelRefetch)
        => FetchInternal(meta, cancelRefetch, options);

    private Task FetchInternal(FetchMeta? meta, bool cancelRefetch, QueryConfiguration<TData>? options = null)
    {
        // Capture volatile references once so another thread cannot clear them
        // between a null check and dereference.
        var op = _activeFetch;
        var fetchTask = _currentFetchTask;
        var state = State;
        TaskCompletionSource<Task>? replacement = null;
        if (state is not null
            && state.FetchStatus is not FetchStatus.Idle
            && op is not null
            && !op.Retryer.IsCancelled
            && fetchTask is not null)
        {
            if (state.Data is not null && cancelRefetch)
            {
                // Unlike TanStack's microtasks, C# fetch completions can run concurrently.
                // Clear _activeFetch before cancelling so even inline completion detects
                // replacement and cannot overwrite the new fetch's state.
                _logger.QueryCancelled(QueryHash, false);
                _activeFetch = null;
                replacement = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                op.Replacement = replacement;
                CancelOperation(op, SilentCancel);
                // Fall through to start new fetch
            }
            else
            {
                op.Retryer.ContinueRetry();

                _logger.QueryFetchDeduplicated(QueryHash);
                _metrics?.QueryFetchDeduplicatedTotal?.Add(1, QueryMetrics.QueryHashTag(QueryHash));
                return fetchTask;
            }
        }

        if (options is not null)
        {
            SetOptions(options);
        }

        _logger.QueryFetchStarted(QueryHash);
        var fetch = FetchCore(meta);
        _currentFetchTask = fetch;
        replacement?.TrySetResult(fetch);
        return fetch;
    }

    private async Task FetchCore(FetchMeta? meta)
    {
        if (_queryFn is null)
        {
            throw new InvalidOperationException("Query function is not set");
        }

        // Capture state before fetch so Cancel(revert: true) can restore it
        _revertState = State;

        _abortSignalConsumed = false;

        // Read the options once so a concurrent SetOptions can't mix two callers' settings.
        var options = Options;

        // Set fetching state (with optional FetchMeta for infinite query direction)
        Dispatch(new FetchAction { Meta = meta });

        // Capture _queryFn to a local — another thread could null it between the
        // null check at the top of FetchCore and the lambda capture below.
        var queryFn = _queryFn!;

        var retryerOptions = new RetryerOptions<TData>
        {
            Fn = ct =>
            {
                var ctx = new QueryFunctionContext(ct, () => _abortSignalConsumed = true, _client, QueryKey!);
                return StopWaitingOnCancel(queryFn(ctx), ct);
            },
            MaxRetries = (options.Retry ?? OptionsDefaulting.FallbackQueryRetry) is > 0 and var r ? r + 1 : 1,
            RetryDelay = options.RetryDelay,
            TimeProvider = _client.TimeProvider,
            LoggerFactory = _client.LoggerFactory,
            Metrics = _metrics,
            RetrySource = "query",
            NetworkMode = options.NetworkMode ?? OptionsDefaulting.FallbackNetworkMode,
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
        var operation = new FetchOperation { Retryer = retryer };
        _activeFetch = operation;

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

            if (retryer.IsCancelled && _activeFetch?.Retryer != retryer)
            {
                _metrics?.QueryFetchCancelledTotal?.Add(1, queryHashTag);
                _logger.QueryFetchCancelled(QueryHash);
                if (operation.Replacement is { } replacedBy)
                {
                    await await replacedBy.Task;
                }
                return;
            }

            sw?.Stop();
            _metrics?.QueryFetchSuccessTotal?.Add(1, queryHashTag);
            _metrics?.QueryFetchDuration?.Record(sw!.Elapsed.TotalSeconds, queryHashTag);

            var now = GetUtcNowMs();
            Dispatch(new SuccessAction<TData> { Data = data, DataUpdatedAt = now, Manual = false });
            _logger.QueryFetchSucceeded(QueryHash);
        }
        catch (OperationCanceledException ex) when (retryer.IsCancelled)
        {
            _metrics?.QueryFetchCancelledTotal?.Add(1, queryHashTag);
            _logger.QueryFetchCancelled(QueryHash);

            if (operation.Replacement is { } replacedBy)
            {
                await await replacedBy.Task;
                return;
            }

            switch (operation.Cancellation)
            {
                case { Silent: true }:
                    throw;
                case { Revert: true }:
                    if (State is not { Data: not null }) throw;
                    return;
                default:
                    Dispatch(new ErrorAction { Error = ex });
                    throw;
            }
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

            operation.Settled.TrySetResult();
        }
    }

    // TanStack retryer.ts cancel rejects without waiting for the query function. WaitAsync
    // gives queries that behavior without making disposal of a mutation's shared Retryer
    // abandon an unfinished write.
    private static async Task<TData> StopWaitingOnCancel(Task<TData> attempt, CancellationToken cancellationToken)
    {
        try
        {
            return await attempt.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!attempt.IsCompleted)
        {
            _ = attempt.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
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
        // The constructor schedules GC before the query enters the cache. A zero-delay timer
        // must not remove a different query cached under the same hash.
        if (observerCount == 0 && State is not null && State.FetchStatus == FetchStatus.Idle
            && QueryHash is not null
            && ReferenceEquals(_cache.GetByHash(QueryHash), this))
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

    private QueryState<TData> ApplySuccess(QueryState<TData> state, SuccessAction<TData> action)
    {
        var newState = state with
        {
            Data = action.Data,
            DataUpdatedAt = action.DataUpdatedAt,
            DataUpdateCount = state.DataUpdateCount + 1,
            Error = null,
            IsInvalidated = false,
            Status = QueryStatus.Succeeded,
        };

        if (!action.Manual)
        {
            newState = newState with
            {
                FetchStatus = FetchStatus.Idle,
                FetchFailureCount = 0,
                FetchFailureReason = null,
            };
        }

        // Save manual writes before notifying observers, which can cancel and revert the fetch.
        _revertState = action.Manual ? newState : null;
        return newState;
    }

    private void Dispatch(DispatchAction action)
    {
        _logger.QueryDispatch(QueryHash, action.GetType().Name);

        QueryState<TData> Reducer(QueryState<TData> state)
        {
            return action switch
            {
                SetStateAction<TData> setStateAction => setStateAction.State,

                SuccessAction<TData> successAction => ApplySuccess(state, successAction),

                FetchAction fetchAction => state with
                {
                    FetchMeta = fetchAction.Meta,
                    IsInvalidated = false,
                    FetchStatus = NetworkModeHelper.CanFetch(Options.NetworkMode ?? OptionsDefaulting.FallbackNetworkMode, _client.OnlineManager)
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

                PauseAction => state with { FetchStatus = FetchStatus.Paused },

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
