using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

public sealed class QueryClient : IDisposable
{
    private readonly QueryCache _queryCache;
    private readonly MutationCache _mutationCache;
    private readonly ILogger _logger;
    private readonly List<QueryDefaults> _queryDefaults = [];
    private readonly List<MutationDefaults> _mutationDefaults = [];
    private QueryClientDefaultOptions? _defaultOptions;
    private bool _disposed;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// The <see cref="System.TimeProvider"/> used by all queries, mutations, and
    /// observers created through this client. Defaults to <see cref="TimeProvider.System"/>
    /// for production use; pass a <c>FakeTimeProvider</c> in tests for deterministic
    /// time control.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// The <see cref="ILoggerFactory"/> used by all queries, mutations, observers,
    /// and MVVM ViewModels created through this client. Defaults to
    /// <see cref="NullLoggerFactory.Instance"/> which discards all log output;
    /// pass a real factory to enable structured diagnostics.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// The <see cref="IFocusManager"/> used to track application focus state.
    /// Defaults to the global <see cref="RabstackQuery.FocusManager.Instance"/>
    /// singleton; pass a dedicated <c>new FocusManager()</c> in tests to avoid
    /// cross-test interference.
    /// </summary>
    public IFocusManager FocusManager { get; }

    /// <summary>
    /// The <see cref="IOnlineManager"/> used to track network connectivity state.
    /// Defaults to the global <see cref="RabstackQuery.OnlineManager.Instance"/>
    /// singleton; pass a dedicated <c>new OnlineManager()</c> in tests to avoid
    /// cross-test interference.
    /// </summary>
    public IOnlineManager OnlineManager { get; }

    /// <summary>
    /// The <see cref="IMeterFactory"/> used to create metrics instruments.
    /// Null when metrics are disabled (no factory provided at construction).
    /// </summary>
    public IMeterFactory? MeterFactory { get; }

    /// <summary>
    /// Centralised metrics instruments. Components that hold a <see cref="QueryClient"/>
    /// reference access this to record measurements. All instruments are null when
    /// <see cref="MeterFactory"/> was not provided.
    /// </summary>
    internal QueryMetrics Metrics { get; }

    /// <summary>
    /// The <see cref="INotifyManager"/> used to batch observer notifications.
    /// Each <see cref="QueryClient"/> owns its own instance so that independent
    /// clients (e.g. separate Blazor Server circuits) do not share batching state.
    /// </summary>
    public INotifyManager NotifyManager { get; }

    public QueryClient(
        QueryCache queryCache,
        MutationCache? mutationCache = null,
        TimeProvider? timeProvider = null,
        IFocusManager? focusManager = null,
        IOnlineManager? onlineManager = null,
        ILoggerFactory? loggerFactory = null,
        IMeterFactory? meterFactory = null)
    {
        ArgumentNullException.ThrowIfNull(queryCache);
        _queryCache = queryCache;
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<QueryClient>();
        _queryCache.SetLoggerFactory(LoggerFactory);
        MeterFactory = meterFactory;
        Metrics = new QueryMetrics(meterFactory);
        _queryCache.SetMetrics(Metrics);
        _mutationCache = mutationCache ?? new MutationCache(null, LoggerFactory);
        _mutationCache.SetLoggerFactory(LoggerFactory);
        NotifyManager = new NotifyManager();
        _queryCache.SetNotifyManager(NotifyManager);
        _mutationCache.SetNotifyManager(NotifyManager);
        TimeProvider = timeProvider ?? TimeProvider.System;
        FocusManager = focusManager ?? RabstackQuery.FocusManager.Instance;
        OnlineManager = onlineManager ?? RabstackQuery.OnlineManager.Instance;

        // Subscribe to focus and online events
        FocusManager.FocusChanged += OnFocusChanged;
        OnlineManager.OnlineChanged += OnOnlineChanged;
    }

    /// <summary>
    /// Removes all queries and mutations from the cache.
    /// Mirrors TanStack's <c>queryClient.clear()</c>.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        _queryCache.Clear();
        _mutationCache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        FocusManager.FocusChanged -= OnFocusChanged;
        OnlineManager.OnlineChanged -= OnOnlineChanged;

        // Clear both caches so scoped instances (e.g. Blazor Server circuits)
        // release all query/mutation state on disposal. For singleton instances
        // this only runs at app shutdown.
        _queryCache.Clear();
        _mutationCache.Clear();
    }

    private void OnFocusChanged(object? sender, EventArgs e)
    {
        _logger.FocusChanged(FocusManager.IsFocused);
        if (!FocusManager.IsFocused)
        {
            return;
        }

        try { ResumePausedMutations(); }
        catch (Exception ex) { _logger.ResumePausedMutationsErrorSwallowed(ex); }

        try { _queryCache.OnFocus(); }
        catch (Exception ex) { _logger.EventHandlerErrorSwallowed("OnFocus", ex); }
    }

    private void OnOnlineChanged(object? sender, EventArgs e)
    {
        _logger.OnlineChanged(OnlineManager.IsOnline);
        if (!OnlineManager.IsOnline)
        {
            return;
        }

        try { ResumePausedMutations(); }
        catch (Exception ex) { _logger.ResumePausedMutationsErrorSwallowed(ex); }

        try { _queryCache.OnOnline(); }
        catch (Exception ex) { _logger.EventHandlerErrorSwallowed("OnOnline", ex); }
    }

    /// <summary>
    /// Resumes all paused mutations. Only has an effect when the client is online.
    /// Matches TanStack's <c>queryClient.resumePausedMutations()</c> at queryClient.ts:450-455.
    /// </summary>
    public void ResumePausedMutations()
    {
        ThrowIfDisposed();
        if (!OnlineManager.IsOnline)
        {
            return;
        }

        _mutationCache.ResumePausedMutations();
    }

    // ── Imperative fetch methods ──────────────────────────────────────

    /// <summary>
    /// Fetches a query imperatively. If the query exists in the cache and is
    /// not stale, returns the cached data without fetching. Otherwise fetches
    /// fresh data. Does not create a persistent observer subscription.
    /// </summary>
    /// <remarks>
    /// Retry defaults to 0 (no retries), matching TanStack's <c>fetchQuery</c>.
    /// Errors are propagated to the caller.
    /// </remarks>
    public async Task<TData> FetchQueryAsync<TData>(
        FetchQueryOptions<TData> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        var queryHash = hasher.HashQueryKey(options.QueryKey);
        _logger.FetchQuery(queryHash);

        var queryOptions = new QueryConfiguration<TData>
        {
            QueryKey = options.QueryKey,
            QueryKeyHasher = hasher,
            GcTime = options.GcTime ?? QueryTimeDefaults.GcTime,
            // FetchQuery defaults to 0 retries (matching TanStack's fetchQuery where retry = false)
            Retry = options.Retry ?? 0,
            RetryDelay = options.RetryDelay,
            Meta = options.Meta,
            NetworkMode = options.NetworkMode ?? NetworkMode.Online,
        };

        var query = _queryCache.GetOrCreate<TData, TData>(this, queryOptions);
        query.SetQueryFn(options.QueryFn);

        // If the cache has fresh data, return it directly.
        // The ! operator is justified: Data is logically present when status is
        // Succeeded, but TData is unconstrained so the compiler can't narrow TData?.
        if (query.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, options.StaleTime ?? TimeSpan.Zero, state.IsInvalidated))
        {
            _logger.FetchQueryCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        Metrics.CacheMissTotal?.Add(1);
        await query.Fetch(cancellationToken);

        Debug.Assert(query.State is not null);
        return query.State.Data!;
    }

    /// <summary>
    /// Like <see cref="FetchQueryAsync{TData}"/> but swallows all exceptions.
    /// Useful for warming the cache in the background.
    /// </summary>
    public async Task PrefetchQueryAsync<TData>(
        FetchQueryOptions<TData> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.PrefetchQuery(queryHash);

        try
        {
            await FetchQueryAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            // Intentionally swallowed — prefetch failures are silent
            _logger.PrefetchQueryErrorSwallowed(queryHash, ex);
        }
    }

    /// <summary>
    /// Returns cached data if it exists and is fresh. Otherwise delegates to
    /// <see cref="FetchQueryAsync{TData}"/> to fetch and cache the data.
    /// </summary>
    public async Task<TData> EnsureQueryDataAsync<TData>(
        FetchQueryOptions<TData> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.EnsureQueryData(queryHash);

        var query = _queryCache.Get<TData>(queryHash);

        if (query?.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, options.StaleTime ?? TimeSpan.Zero, state.IsInvalidated))
        {
            _logger.EnsureQueryDataCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        // If InitialData is provided and there's no cached data, seed the cache
        // and return it immediately — optionally triggering a background refetch.
        if (options.InitialData is not null && query?.State?.Status is not QueryStatus.Succeeded)
        {
            SetQueryData(options.QueryKey, options.InitialData);

            if (options.RevalidateIfStale)
            {
                // Fire-and-forget background refetch — caller gets InitialData immediately.
                _ = FetchQueryAsync(options, cancellationToken).ContinueWith(
                    static t => { /* swallow — background revalidation is best-effort */ },
                    TaskScheduler.Default);
            }

            return options.InitialData;
        }

        // If there IS cached data but it's stale and RevalidateIfStale is set,
        // return the stale data immediately and kick off a background refetch.
        if (options.RevalidateIfStale
            && query?.State is { Status: QueryStatus.Succeeded } staleState)
        {
            _ = FetchQueryAsync(options, cancellationToken).ContinueWith(
                static t => { /* swallow — background revalidation is best-effort */ },
                TaskScheduler.Default);

            return staleState.Data!;
        }

        // Cache miss is recorded inside FetchQueryAsync when it proceeds to fetch.
        return await FetchQueryAsync(options, cancellationToken);
    }

    // ── Imperative infinite query fetch methods ─────────────────────────

    /// <summary>
    /// Fetches an infinite query imperatively. If the query exists in the cache
    /// and is not stale, returns the cached data without fetching. Otherwise
    /// fetches the first page. Does not create a persistent observer subscription.
    /// </summary>
    /// <remarks>
    /// Retry defaults to 0 (no retries), matching TanStack's <c>fetchQuery</c>.
    /// Errors are propagated to the caller.
    /// </remarks>
    public async Task<InfiniteData<TData, TPageParam>> FetchInfiniteQueryAsync<TData, TPageParam>(
        FetchInfiniteQueryOptions<TData, TPageParam> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        var queryHash = hasher.HashQueryKey(options.QueryKey);
        _logger.FetchInfiniteQuery(queryHash);

        var queryOptions = new QueryConfiguration<InfiniteData<TData, TPageParam>>
        {
            QueryKey = options.QueryKey,
            QueryKeyHasher = hasher,
            GcTime = options.GcTime ?? QueryTimeDefaults.GcTime,
            // FetchInfiniteQuery defaults to 0 retries (matching TanStack's fetchQuery where retry = false)
            Retry = options.Retry ?? 0
        };

        var query = _queryCache.GetOrCreate<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>>(this, queryOptions);

        // Build the InfiniteQueryObserverOptions that CreateFetchFn needs to wire
        // up page-fetching logic (QueryFn, InitialPageParam, GetNextPageParam, etc.).
        var observerOptions = new InfiniteQueryObserverOptions<TData, TPageParam>
        {
            QueryKey = options.QueryKey,
            QueryFn = options.QueryFn,
            InitialPageParam = options.InitialPageParam,
            GetNextPageParam = options.GetNextPageParam,
            GetPreviousPageParam = options.GetPreviousPageParam,
            MaxPages = options.MaxPages,
        };

        query.SetQueryFn(InfiniteQueryBehavior.CreateFetchFn(observerOptions, query));

        // If the cache has fresh data, return it directly.
        if (query.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, options.StaleTime ?? TimeSpan.Zero, state.IsInvalidated))
        {
            _logger.FetchInfiniteQueryCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        Metrics.CacheMissTotal?.Add(1);
        await query.Fetch(cancellationToken);

        Debug.Assert(query.State is not null);
        return query.State.Data!;
    }

    /// <summary>
    /// Like <see cref="FetchInfiniteQueryAsync{TData,TPageParam}"/> but swallows all
    /// exceptions. Useful for warming the cache in the background.
    /// </summary>
    public async Task PrefetchInfiniteQueryAsync<TData, TPageParam>(
        FetchInfiniteQueryOptions<TData, TPageParam> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.PrefetchInfiniteQuery(queryHash);

        try
        {
            await FetchInfiniteQueryAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            // Intentionally swallowed — prefetch failures are silent
            _logger.PrefetchInfiniteQueryErrorSwallowed(queryHash, ex);
        }
    }

    /// <summary>
    /// Returns cached infinite query data if it exists and is fresh. Otherwise
    /// delegates to <see cref="FetchInfiniteQueryAsync{TData,TPageParam}"/> to fetch
    /// and cache the data.
    /// </summary>
    public async Task<InfiniteData<TData, TPageParam>> EnsureInfiniteQueryDataAsync<TData, TPageParam>(
        FetchInfiniteQueryOptions<TData, TPageParam> options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.EnsureInfiniteQueryData(queryHash);

        var query = _queryCache.Get<InfiniteData<TData, TPageParam>>(queryHash);

        if (query?.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, options.StaleTime ?? TimeSpan.Zero, state.IsInvalidated))
        {
            _logger.EnsureInfiniteQueryDataCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        // If InitialData is provided and there's no cached data, seed the cache
        // and return it immediately — optionally triggering a background refetch.
        if (options.InitialData is not null && query?.State?.Status is not QueryStatus.Succeeded)
        {
            SetQueryData(options.QueryKey, options.InitialData);

            if (options.RevalidateIfStale)
            {
                // Fire-and-forget background refetch — caller gets InitialData immediately.
                _ = FetchInfiniteQueryAsync(options, cancellationToken).ContinueWith(
                    static t => { /* swallow — background revalidation is best-effort */ },
                    TaskScheduler.Default);
            }

            return options.InitialData;
        }

        // If there IS cached data but it's stale and RevalidateIfStale is set,
        // return the stale data immediately and kick off a background refetch.
        if (options.RevalidateIfStale
            && query?.State is { Status: QueryStatus.Succeeded } staleState)
        {
            _ = FetchInfiniteQueryAsync(options, cancellationToken).ContinueWith(
                static t => { /* swallow — background revalidation is best-effort */ },
                TaskScheduler.Default);

            return staleState.Data!;
        }

        // Cache miss is recorded inside FetchInfiniteQueryAsync when it proceeds to fetch.
        return await FetchInfiniteQueryAsync(options, cancellationToken);
    }

    private bool IsDataStale(long dataUpdatedAt, TimeSpan staleTime, bool isInvalidated = false)
        => QueryTimeDefaults.IsStale(dataUpdatedAt, staleTime, isInvalidated, TimeProvider.GetUtcNowMs());

    // ── Accessors ──────────────────────────────────────────────────

    public QueryCache QueryCache
    {
        get { ThrowIfDisposed(); return _queryCache; }
    }

    public MutationCache MutationCache
    {
        get { ThrowIfDisposed(); return _mutationCache; }
    }

    /// <summary>
    /// Global default options that apply to all queries.
    /// </summary>
    public QueryClientDefaultOptions? DefaultOptions
    {
        get { ThrowIfDisposed(); return _defaultOptions; }
        set { ThrowIfDisposed(); _defaultOptions = value; }
    }

    /// <summary>
    /// Registers per-key-prefix defaults. Any query whose key starts with
    /// <paramref name="queryKey"/> will inherit these defaults (unless
    /// overridden by per-query options).
    /// </summary>
    public void SetQueryDefaults(QueryKey queryKey, QueryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ArgumentNullException.ThrowIfNull(defaults);
        ThrowIfDisposed();
        // Replace existing defaults for this exact key, or append
        var index = _queryDefaults.FindIndex(d =>
            QueryKeyMatcher.ExactMatchKey(d.QueryKey, queryKey));

        var entry = new QueryDefaults
        {
            QueryKey = queryKey,
            StaleTime = defaults.StaleTime,
            GcTime = defaults.GcTime,
            Retry = defaults.Retry,
            RetryDelay = defaults.RetryDelay,
            RefetchOnWindowFocus = defaults.RefetchOnWindowFocus,
            RefetchOnReconnect = defaults.RefetchOnReconnect,
            RefetchOnMount = defaults.RefetchOnMount
        };

        if (index >= 0)
        {
            _queryDefaults[index] = entry;
        }
        else
        {
            _queryDefaults.Add(entry);
        }
    }

    public QueryObserverOptions<TData, TQueryData> DefaultQueryOptions<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
    {
        ThrowIfDisposed();
        if (options.QueryKey is null)
        {
            throw new ArgumentException("QueryKey must be provided.", nameof(options));
        }

        var queryKey = options.QueryKey;
        var keyDefaults = GetMergedQueryDefaults(queryKey);

        // Resolve StaleTime through the three-level merge:
        // per-query → per-key-prefix → global → framework default
        var resolvedStaleTime = options.StaleTime;
        if (resolvedStaleTime == TimeSpan.Zero)
        {
            resolvedStaleTime = keyDefaults.StaleTime ?? _defaultOptions?.StaleTime ?? TimeSpan.Zero;
        }

        // Resolve RefetchOnWindowFocus / RefetchOnReconnect through the same merge.
        // We can't distinguish "explicitly set to WhenStale" from "left at default",
        // so we only override from defaults when the per-query value is WhenStale.
        var refetchOnWindowFocus = options.RefetchOnWindowFocus is not RefetchOnBehavior.WhenStale
            ? options.RefetchOnWindowFocus
            : keyDefaults.RefetchOnWindowFocus ?? RefetchOnBehavior.WhenStale;

        var refetchOnReconnect = options.RefetchOnReconnect is not RefetchOnBehavior.WhenStale
            ? options.RefetchOnReconnect
            : keyDefaults.RefetchOnReconnect ?? RefetchOnBehavior.WhenStale;

        var refetchOnMount = options.RefetchOnMount is not RefetchOnBehavior.WhenStale
            ? options.RefetchOnMount
            : keyDefaults.RefetchOnMount ?? RefetchOnBehavior.WhenStale;

        return new QueryObserverOptions<TData, TQueryData>
        {
            QueryKey = queryKey,
            Enabled = SkipToken.IsSkipToken(options.QueryFn) ? false : options.Enabled,
            EnabledFn = options.EnabledFn,
            Select = options.Select,
            StaleTime = resolvedStaleTime,
            StaleTimeFn = options.StaleTimeFn,
            GcTime = options.GcTime,
            QueryFn = options.QueryFn,
            RefetchOnWindowFocus = refetchOnWindowFocus,
            RefetchOnReconnect = refetchOnReconnect,
            RefetchOnMount = refetchOnMount,
            RefetchInterval = options.RefetchInterval,
            RefetchIntervalFn = options.RefetchIntervalFn,
            RefetchIntervalInBackground = options.RefetchIntervalInBackground,
            PlaceholderData = options.PlaceholderData,
            NetworkMode = options.NetworkMode
        };
    }

    /// <summary>
    /// Merges global defaults and per-key-prefix defaults into the given
    /// <see cref="QueryOptions{TData}"/>. Three-level merge:
    /// per-query options → per-key-prefix defaults → global defaults → framework defaults.
    /// </summary>
    public QueryConfiguration<TData> DefaultQueryOptions<TData>(QueryConfiguration<TData> options)
    {
        ThrowIfDisposed();
        if (options.QueryKey is null)
        {
            return options;
        }

        var keyDefaults = GetMergedQueryDefaults(options.QueryKey);

        // GcTime: per-query wins if explicitly set (non-zero), else key defaults, else global
        var gcTime = options.GcTime > TimeSpan.Zero
            ? options.GcTime
            : keyDefaults.GcTime ?? _defaultOptions?.GcTime ?? QueryTimeDefaults.GcTime;

        // Retry: null means "not explicitly set" — fall through to key/global defaults.
        var retry = options.Retry
            ?? keyDefaults.Retry
            ?? _defaultOptions?.Retry
            ?? 3;

        var retryDelay = options.RetryDelay
            ?? keyDefaults.RetryDelay
            ?? _defaultOptions?.RetryDelay;

        var result = new QueryConfiguration<TData>
        {
            QueryKey = options.QueryKey,
            QueryHash = options.QueryHash,
            QueryKeyHasher = options.QueryKeyHasher,
            // InitialData is set conditionally below to preserve HasInitialData semantics
            InitialDataFactory = options.InitialDataFactory,
            InitialDataUpdatedAt = options.InitialDataUpdatedAt,
            InitialDataUpdatedAtFactory = options.InitialDataUpdatedAtFactory,
            GcTime = gcTime,
            Retry = retry,
            RetryDelay = retryDelay,
            NetworkMode = options.NetworkMode,
            Meta = options.Meta,
        };

        // Only propagate InitialData when the source explicitly set it. Assigning
        // the property (even to default) sets HasInitialData=true, which would cause
        // value-type queries to start as Succeeded instead of Pending.
        if (options.HasInitialData)
        {
            result.InitialData = options.InitialData;
        }

        return result;
    }

    public QueryConfiguration<TData>? GetQueryDefaults<TData>(QueryKey queryKey)
    {
        ThrowIfDisposed();
        var merged = GetMergedQueryDefaults(queryKey);

        return new QueryConfiguration<TData>
        {
            GcTime = merged.GcTime ?? QueryTimeDefaults.GcTime,
            Retry = merged.Retry,
            RetryDelay = merged.RetryDelay
        };
    }

    /// <summary>
    /// Iterates all registered per-key-prefix defaults whose key is a prefix
    /// of <paramref name="queryKey"/> and merges them in registration order.
    /// Later registrations override earlier ones for the same property.
    /// </summary>
    private QueryDefaults GetMergedQueryDefaults(QueryKey queryKey)
    {
        TimeSpan? staleTime = null;
        TimeSpan? gcTime = null;
        int? retry = null;
        Func<int, Exception, TimeSpan>? retryDelay = null;
        RefetchOnBehavior? refetchOnWindowFocus = null;
        RefetchOnBehavior? refetchOnReconnect = null;
        RefetchOnBehavior? refetchOnMount = null;

        foreach (var defaults in _queryDefaults)
        {
            if (!QueryKeyMatcher.PartialMatchKey(queryKey, defaults.QueryKey))
            {
                continue;
            }

            staleTime = defaults.StaleTime ?? staleTime;
            gcTime = defaults.GcTime ?? gcTime;
            retry = defaults.Retry ?? retry;
            retryDelay = defaults.RetryDelay ?? retryDelay;
            refetchOnWindowFocus = defaults.RefetchOnWindowFocus ?? refetchOnWindowFocus;
            refetchOnReconnect = defaults.RefetchOnReconnect ?? refetchOnReconnect;
            refetchOnMount = defaults.RefetchOnMount ?? refetchOnMount;
        }

        return new QueryDefaults
        {
            QueryKey = queryKey,
            StaleTime = staleTime,
            GcTime = gcTime,
            Retry = retry,
            RetryDelay = retryDelay,
            RefetchOnWindowFocus = refetchOnWindowFocus,
            RefetchOnReconnect = refetchOnReconnect,
            RefetchOnMount = refetchOnMount
        };
    }

    // ── Mutation defaults ───────────────────────────────────────────────

    /// <summary>
    /// Registers per-key-prefix defaults for mutations. Any mutation whose key
    /// starts with <paramref name="mutationKey"/> will inherit these defaults
    /// (unless overridden by per-mutation options).
    /// </summary>
    public void SetMutationDefaults(QueryKey mutationKey, MutationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(mutationKey);
        ArgumentNullException.ThrowIfNull(defaults);
        ThrowIfDisposed();
        // Replace existing defaults for this exact key, or append
        var index = _mutationDefaults.FindIndex(d =>
            QueryKeyMatcher.ExactMatchKey(d.MutationKey, mutationKey));

        var entry = new MutationDefaults
        {
            MutationKey = mutationKey,
            Retry = defaults.Retry,
            RetryDelay = defaults.RetryDelay,
            GcTime = defaults.GcTime,
            NetworkMode = defaults.NetworkMode,
        };

        if (index >= 0)
        {
            _mutationDefaults[index] = entry;
        }
        else
        {
            _mutationDefaults.Add(entry);
        }
    }

    /// <summary>
    /// Returns the merged per-key-prefix mutation defaults for a given key.
    /// All defaults whose key is a prefix of <paramref name="mutationKey"/>
    /// are merged in registration order. Returns null when no defaults match.
    /// </summary>
    public MutationDefaults? GetMutationDefaults(QueryKey mutationKey)
    {
        ThrowIfDisposed();
        var merged = GetMergedMutationDefaults(mutationKey);
        if (merged is null)
        {
            return null;
        }

        return merged;
    }

    /// <summary>
    /// Merges per-key-prefix mutation defaults into the given options.
    /// Short-circuits if the options have already been defaulted.
    /// </summary>
    public MutationOptions<TData, TError, TVariables, TOnMutateResult>
        DefaultMutationOptions<TData, TError, TVariables, TOnMutateResult>(
            MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
        where TError : Exception
    {
        ThrowIfDisposed();
        if (options.Defaulted)
        {
            return options;
        }

        var mutationKey = options.MutationKey;
        var keyDefaults = mutationKey is not null ? GetMergedMutationDefaults(mutationKey) : null;

        // Retry: null means "not explicitly set" — fall through to key/global defaults.
        var retry = options.Retry
            ?? keyDefaults?.Retry
            ?? 0;

        var retryDelay = options.RetryDelay ?? keyDefaults?.RetryDelay;

        var gcTime = options.GcTime != QueryTimeDefaults.GcTime
            ? options.GcTime
            : keyDefaults?.GcTime ?? QueryTimeDefaults.GcTime;

        var networkMode = options.NetworkMode != NetworkMode.Online
            ? options.NetworkMode
            : keyDefaults?.NetworkMode ?? NetworkMode.Online;

        return options with
        {
            NetworkMode = networkMode,
            Retry = retry,
            RetryDelay = retryDelay,
            GcTime = gcTime,
            Defaulted = true,
        };
    }

    /// <summary>
    /// Iterates all registered per-key-prefix mutation defaults whose key is a
    /// prefix of <paramref name="mutationKey"/> and merges them in registration order.
    /// Returns null when no defaults match.
    /// </summary>
    private MutationDefaults? GetMergedMutationDefaults(QueryKey mutationKey)
    {
        int? retry = null;
        Func<int, Exception, TimeSpan>? retryDelay = null;
        TimeSpan? gcTime = null;
        NetworkMode? networkMode = null;
        var found = false;

        foreach (var defaults in _mutationDefaults)
        {
            if (!QueryKeyMatcher.PartialMatchKey(mutationKey, defaults.MutationKey))
            {
                continue;
            }

            found = true;
            retry = defaults.Retry ?? retry;
            retryDelay = defaults.RetryDelay ?? retryDelay;
            gcTime = defaults.GcTime ?? gcTime;
            networkMode = defaults.NetworkMode ?? networkMode;
        }

        if (!found)
        {
            return null;
        }

        return new MutationDefaults
        {
            MutationKey = mutationKey,
            Retry = retry,
            RetryDelay = retryDelay,
            GcTime = gcTime,
            NetworkMode = networkMode,
        };
    }

    // ── Bulk query operations with filter support ─────────────────────

    /// <summary>
    /// Invalidates all queries matching the given filters, then refetches
    /// according to <see cref="InvalidateQueryFilters.RefetchType"/>.
    /// The returned task completes when all triggered refetches finish.
    /// Matches TanStack's async <c>invalidateQueries</c> semantics.
    /// </summary>
    public async Task InvalidateQueriesAsync(
        InvalidateQueryFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.InvalidateQueries();

        // Batch<Task> starts refetch tasks inside the batch (so invalidation +
        // fetch-start notifications are batched together) and returns the Task
        // to await outside the batch.
        var refetchTask = NotifyManager.Batch(() =>
        {
            foreach (var query in _queryCache.FindAll(filters))
            {
                query.Invalidate();
            }

            if (filters?.RefetchType is InvalidateRefetchType.None)
            {
                return Task.CompletedTask;
            }

            // Map InvalidateRefetchType → QueryTypeFilter for the refetch pass.
            // Default: Active (only queries with observers), matching TanStack's
            // `refetchType ?? 'active'`. The inherited QueryFilters.Type property
            // controls which queries are invalidated; RefetchType independently
            // controls which invalidated queries are refetched.
            var refetchTypeFilter = filters?.RefetchType switch
            {
                InvalidateRefetchType.Inactive => QueryTypeFilter.Inactive,
                InvalidateRefetchType.All => QueryTypeFilter.All,
                _ => QueryTypeFilter.Active
            };

            return RefetchQueriesAsync(
                new QueryFilters
                {
                    QueryKey = filters?.QueryKey,
                    Exact = filters?.Exact ?? false,
                    Type = refetchTypeFilter,
                    Stale = filters?.Stale,
                    FetchStatus = filters?.FetchStatus,
                    Predicate = filters?.Predicate
                },
                cancellationToken);
        });

        await refetchTask;
    }

    /// <summary>Convenience overload that does a prefix match on the key.</summary>
    public Task InvalidateQueriesAsync(QueryKey queryKey, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = queryKey }, cancellationToken);
    }

    /// <summary>
    /// Refetches all queries.
    /// </summary>
    public Task RefetchQueriesAsync(CancellationToken cancellationToken = default)
        => RefetchQueriesAsync(filters: null, cancellationToken);

    /// <summary>
    /// Refetches all queries matching the given filters.
    /// This immediately triggers a fetch regardless of stale status.
    /// </summary>
    public Task RefetchQueriesAsync(QueryFilters? filters, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return RefetchQueriesAsync(filters, options: null, cancellationToken);
    }

    /// <summary>
    /// Refetches all queries matching the given filters, with explicit
    /// <see cref="RefetchOptions"/> for <c>CancelRefetch</c> and <c>ThrowOnError</c>.
    /// Mirrors TanStack <c>queryClient.ts:319–338</c>.
    /// </summary>
    public async Task RefetchQueriesAsync(QueryFilters? filters, RefetchOptions? options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.RefetchQueries();
        var cancelRefetch = options?.CancelRefetch ?? true;

        // Skip disabled and static queries. Mirrors TanStack queryClient.ts:326
        // which filters with `!query.isDisabled() && !query.isStatic()`.
        var tasks = _queryCache.FindAll(filters)
            .Where(q => !q.IsDisabled() && !q.IsStatic())
            .Select(q =>
            {
                var fetchTask = q.Fetch(cancelRefetch, cancellationToken);

                // TanStack queryClient.ts:332–334 — resolve immediately for paused
                // queries. Their retryer blocks on a TCS until the network is
                // restored, so awaiting them would hang RefetchQueriesAsync indefinitely.
                // The check works because Retryer.ExecuteAsync fires OnPause
                // synchronously before its first suspension point, so FetchStatus is
                // already Paused by the time Fetch() returns the incomplete task.
                if (q.CurrentFetchStatus is FetchStatus.Paused)
                {
                    return Task.CompletedTask;
                }

                // TanStack queryClient.ts:332–337 — when throwOnError is true,
                // let errors propagate through Task.WhenAll. Otherwise swallow
                // per-query errors so one failure doesn't abort the batch.
                if (options?.ThrowOnError is true)
                {
                    return fetchTask;
                }

                // Matches TanStack's .catch(noop). Accessing t.Exception marks the
                // exception as observed, preventing UnobservedTaskException events
                // (e.g. from SetQueryData queries that have no query function).
                return fetchTask.ContinueWith(
                    static t => { _ = t.Exception; },
                    TaskScheduler.Default);
            });
        await Task.WhenAll(tasks);
    }

    /// <summary>Backward-compatible overload that does a prefix match on the key.</summary>
    public async Task RefetchQueriesAsync(QueryKey queryKey, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await RefetchQueriesAsync(new QueryFilters { QueryKey = queryKey }, cancellationToken);
    }

    /// <summary>
    /// Removes all queries from the cache.
    /// </summary>
    public void RemoveQueries() => RemoveQueries(null);

    /// <summary>
    /// Removes all queries matching the given filters from the cache.
    /// </summary>
    public void RemoveQueries(QueryFilters? filters)
    {
        ThrowIfDisposed();
        _logger.RemoveQueries();
        NotifyManager.Batch(() =>
        {
            foreach (var query in _queryCache.FindAll(filters).ToList())
            {
                _queryCache.Remove(query);
            }
        });
    }

    /// <summary>
    /// Resets all queries to their initial state.
    /// Active queries (with observers) are refetched after reset.
    /// </summary>
    public void ResetQueries() => ResetQueries(null);

    /// <summary>
    /// Resets all queries matching the given filters to their initial state.
    /// Active queries (with observers) are refetched after reset.
    /// </summary>
    public void ResetQueries(QueryFilters? filters)
    {
        ThrowIfDisposed();
        _logger.ResetQueries();
        NotifyManager.Batch(() =>
        {
            foreach (var query in _queryCache.FindAll(filters))
            {
                query.Reset();

                // Active queries should refetch after reset
                if (query.IsActive())
                {
                    _ = query.Fetch();
                }
            }
        });
    }

    /// <summary>
    /// Returns the data for all queries matching the given filters as
    /// (QueryKey, TData) tuples.
    /// </summary>
    /// <inheritdoc cref="GetQueriesData{TData}(QueryFilters?)"/>
    // TData? in the tuple is unavoidable: QueryState<TData>.Data is TData?
    // and the generic is unconstrained, so the compiler can't narrow it.
    public IEnumerable<(QueryKey Key, TData? Data)> GetQueriesData<TData>()
        => GetQueriesData<TData>(null);

    /// <inheritdoc/>
    // TData? in the tuple is unavoidable: QueryState<TData>.Data is TData?
    // and the generic is unconstrained, so the compiler can't narrow it.
    public IEnumerable<(QueryKey Key, TData? Data)> GetQueriesData<TData>(QueryFilters? filters)
    {
        ThrowIfDisposed();
        foreach (var query in _queryCache.FindAll(filters).OfType<Query<TData>>())
        {
            if (query.QueryKey is not { } key)
            {
                continue;
            }

            yield return (key, query.State is not null ? query.State.Data : default);
        }
    }

    /// <summary>
    /// Updates data for all queries matching the given filters using the updater function.
    /// </summary>
    public void SetQueriesData<TData>(QueryFilters filters, Func<TData?, TData> updater)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(updater);
        ThrowIfDisposed();
        foreach (var query in _queryCache.FindAll(filters).OfType<Query<TData>>())
        {
            if (query.QueryKey is not { } key)
            {
                continue;
            }

            var current = query.State is not null ? query.State.Data : default;
            SetQueryData(key, updater(current));
        }
    }

    /// <summary>
    /// Returns the number of queries currently fetching.
    /// </summary>
    public int IsFetching() => IsFetching(null);

    /// <summary>
    /// Returns the number of queries currently fetching that match the filters.
    /// </summary>
    public int IsFetching(QueryFilters? filters)
    {
        ThrowIfDisposed();
        var fetchingFilter = new QueryFilters
        {
            QueryKey = filters?.QueryKey,
            Exact = filters?.Exact ?? false,
            Type = filters?.Type ?? QueryTypeFilter.All,
            Stale = filters?.Stale,
            FetchStatus = FetchStatus.Fetching,
            Predicate = filters?.Predicate
        };

        return _queryCache.FindAll(fetchingFilter).Count();
    }

    /// <summary>
    /// Returns the number of mutations currently pending.
    /// </summary>
    public int IsMutating() => IsMutating(null);

    /// <summary>
    /// Returns the number of mutations currently pending that match the filters.
    /// </summary>
    public int IsMutating(MutationFilters? filters)
    {
        ThrowIfDisposed();
        var pendingFilter = filters is not null
            ? new MutationFilters
            {
                MutationKey = filters.MutationKey,
                Exact = filters.Exact,
                Status = filters.Status ?? MutationStatus.Pending,
                Predicate = filters.Predicate
            }
            : new MutationFilters { Status = MutationStatus.Pending };

        return _mutationCache.FindAll(pendingFilter).Count();
    }

    /// <summary>
    /// Manually updates query data in the cache.
    /// Useful for optimistic updates or seeding cache with data from other sources.
    /// </summary>
    /// <typeparam name="TData">The type of data to set.</typeparam>
    /// <param name="queryKey">The query key to update.</param>
    /// <param name="data">The data to set in the cache.</param>
    /// <example>
    /// <code>
    /// // Optimistic update
    /// var addTodoMutation = client.UseMutation&lt;Todo, Todo&gt;(
    ///     async (newTodo, ct) => await api.CreateTodo(newTodo, ct),
    ///     new MutationOptions&lt;Todo, Todo&gt;
    ///     {
    ///         OnMutate = async (newTodo) =>
    ///         {
    ///             // Optimistically add to cache
    ///             var todos = client.GetQueryData&lt;List&lt;Todo&gt;&gt;(["todos"]) ?? new List&lt;Todo&gt;();
    ///             todos.Add(newTodo);
    ///             client.SetQueryData(["todos"], todos);
    ///         },
    ///         OnError = (error, variables) =>
    ///         {
    ///             // Rollback on error
    ///             await client.InvalidateQueriesAsync(["todos"]);
    ///         }
    ///     }
    /// );
    /// </code>
    /// </example>
    // TODO: TanStack applies structuralSharing in Query.setData() (query.ts:231)
    // before dispatching the success state. In C#, SetQueryData dispatches state
    // directly. Observer-level structural sharing in UpdateResult() covers the
    // same data path for subscribed observers. Query-level integration would
    // require threading the option through QueryConfiguration.
    public void SetQueryData<TData>(QueryKey queryKey, TData data)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ThrowIfDisposed();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        _logger.SetQueryData(queryHash, existed: _queryCache.Get<TData>(queryHash) is not null);

        var query = _queryCache.Get<TData>(queryHash);

        if (query is not null)
        {
            // TanStack's setQueryData is a no-op when data is undefined (null in C#).
            // This prevents accidental creation of Succeeded queries with null data
            // and avoids overwriting existing data with null.
            if (data is null)
            {
                return;
            }

            var now = TimeProvider.GetUtcNowMs();

            var newState = new QueryState<TData>
            {
                Data = data,
                DataUpdateCount = (query.State?.DataUpdateCount ?? 0) + 1,
                DataUpdatedAt = now,
                Error = null,
                ErrorUpdateCount = query.State?.ErrorUpdateCount ?? 0,
                ErrorUpdatedAt = query.State?.ErrorUpdatedAt ?? 0,
                FetchFailureCount = 0,
                FetchFailureReason = null,
                FetchMeta = query.State?.FetchMeta,
                IsInvalidated = false,
                Status = QueryStatus.Succeeded,
                // Preserve FetchStatus during an active fetch. TanStack does not
                // reset fetchStatus when setQueryData is called mid-fetch — the
                // in-flight fetch continues and its completion sets the status.
                FetchStatus = query.State?.FetchStatus ?? FetchStatus.Idle
            };

            // SetState is internal (not private) so we can call it directly.
            // This avoids reflection, which is not trim-safe or AOT-compatible.
            // SetQueryData is an intentional bypass of the reducer pattern for
            // manual cache updates — making SetState internal is honest about that.
            query.SetState(newState);
        }
        else
        {
            // TanStack's setQueryData is a no-op when data is undefined for a
            // nonexistent query — it does not create a cache entry with null data.
            if (data is null)
            {
                return;
            }

            // Query doesn't exist yet, create it via Build with an explicit initial
            // state. Deliberately omit InitialData on the options — data set through
            // SetQueryData is not "initial data" in the TanStack sense. Keeping
            // InitialData unset means GetDefaultState(Options) produces Status=Pending
            // with Data=null, which is the correct reset target when Reset() is called.
            var options = new QueryConfiguration<TData>
            {
                QueryKey = queryKey,
                QueryHash = queryHash,
                GcTime = QueryTimeDefaults.GcTime
            };

            var now = TimeProvider.GetUtcNowMs();

            var initialState = new QueryState<TData>
            {
                Data = data,
                DataUpdateCount = 1,
                DataUpdatedAt = now,
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

            _queryCache.GetOrCreate<TData, TData>(this, options, initialState);
        }
    }

    public void SetQueryData<TData>(QueryKey queryKey, Func<TData?, TData> updater)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ArgumentNullException.ThrowIfNull(updater);
        ThrowIfDisposed();
        var currentData = GetQueryData<TData>(queryKey);
        var newData = updater(currentData);
        SetQueryData(queryKey, newData);
    }

    /// <summary>
    /// Gets the current cached data for a query.
    /// Returns default(TData) if the query doesn't exist or has no data.
    /// </summary>
    /// <typeparam name="TData">The type of data to retrieve.</typeparam>
    /// <param name="queryKey">The query key to get data for.</param>
    /// <returns>The cached data, or default(TData) if not found.</returns>
    /// <example>
    /// <code>
    /// var todos = client.GetQueryData&lt;List&lt;Todo&gt;&gt;(["todos"]);
    /// if (todos is not null)
    /// {
    ///     Console.WriteLine($"Found {todos.Count} todos in cache");
    /// }
    /// </code>
    /// </example>
    public TData GetQueryData<TData>(QueryKey queryKey)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ThrowIfDisposed();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);

        var query = _queryCache.Get<TData>(queryHash);

        return query?.State is null ? default! : query.State.Data!;
    }

    // ── QueryOptions<TData> overloads ─────────────────────────────────
    // Bridge methods that delegate to existing key-based methods via
    // QueryOptions<TData>.QueryKey, providing TData inference from the
    // options object. Mirrors TanStack v5's queryOptions() pattern.

    /// <summary>
    /// Gets the current cached data for a query identified by <paramref name="options"/>.
    /// <typeparamref name="TData"/> is inferred from the options object.
    /// </summary>
    public TData GetQueryData<TData>(QueryOptions<TData> options)
    {
        ThrowIfDisposed();
        return GetQueryData<TData>(options.QueryKey);
    }

    /// <summary>
    /// Sets (or creates) cached data for a query identified by <paramref name="options"/>.
    /// <typeparamref name="TData"/> is inferred from the options object.
    /// </summary>
    public void SetQueryData<TData>(QueryOptions<TData> options, TData data)
    {
        ThrowIfDisposed();
        SetQueryData(options.QueryKey, data);
    }

    /// <summary>
    /// Updates cached data for a query identified by <paramref name="options"/>
    /// using a functional updater.
    /// </summary>
    public void SetQueryData<TData>(QueryOptions<TData> options, Func<TData?, TData> updater)
    {
        ThrowIfDisposed();
        SetQueryData(options.QueryKey, updater);
    }

    /// <summary>
    /// Fetches a query imperatively using the key and function from <paramref name="options"/>.
    /// </summary>
    public Task<TData> FetchQueryAsync<TData>(QueryOptions<TData> options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return FetchQueryAsync(options.ToFetchQueryOptions(), cancellationToken);
    }

    /// <summary>
    /// Prefetches a query using the key and function from <paramref name="options"/>.
    /// Errors are swallowed — useful for warming the cache in the background.
    /// </summary>
    public Task PrefetchQueryAsync<TData>(QueryOptions<TData> options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return PrefetchQueryAsync(options.ToFetchQueryOptions(), cancellationToken);
    }

    /// <summary>
    /// Returns cached data if fresh, otherwise fetches using <paramref name="options"/>.
    /// </summary>
    public Task<TData> EnsureQueryDataAsync<TData>(QueryOptions<TData> options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return EnsureQueryDataAsync(options.ToFetchQueryOptions(), cancellationToken);
    }

    /// <summary>
    /// Cancels in-flight fetches for all queries, reverting state to pre-fetch snapshots.
    /// </summary>
    public Task CancelQueriesAsync() => CancelQueriesAsync(null, null);

    /// <summary>
    /// Cancels in-flight fetches for all queries matching the given filters,
    /// reverting state to pre-fetch snapshots.
    /// </summary>
    public Task CancelQueriesAsync(QueryFilters? filters) => CancelQueriesAsync(filters, null);

    /// <summary>
    /// Cancels in-flight fetches for all queries matching the given filters.
    /// By default, reverts query state to its pre-fetch snapshot.
    /// </summary>
    public async Task CancelQueriesAsync(QueryFilters? filters, CancelOptions? options)
    {
        ThrowIfDisposed();
        _logger.CancelQueries();
        var cancelOptions = options ?? new CancelOptions { Revert = true };
        var tasks = _queryCache.FindAll(filters)
            .Select(q => q.Cancel(cancelOptions));
        await Task.WhenAll(tasks);
    }

    /// <summary>Convenience overload that cancels queries by key.</summary>
    public Task CancelQueriesAsync(QueryKey queryKey)
    {
        ThrowIfDisposed();
        return CancelQueriesAsync(new QueryFilters { QueryKey = queryKey });
    }

    // ── Hydration / Dehydration ──────────────────────────────────────

    /// <summary>
    /// Creates a serializable snapshot of the client's cache state.
    /// By default, only succeeded non-placeholder queries and paused mutations
    /// are included. Use <paramref name="options"/> or client-level
    /// <see cref="QueryClientDefaultOptions.Dehydrate"/> to customise filtering.
    /// </summary>
    /// <remarks>
    /// Deliberate C# divergence: no promise/Task dehydration (Tasks can't transfer
    /// across processes). Data transformers (<see cref="DehydrateOptions.SerializeData"/>)
    /// and error redaction (<see cref="DehydrateOptions.ShouldRedactErrors"/>) are
    /// supported; see <see cref="HydrateOptions.DeserializeData"/> for the reverse path.
    /// </remarks>
    /// <inheritdoc cref="Dehydrate(DehydrateOptions?)"/>
    public DehydratedState Dehydrate() => Dehydrate(null);

    /// <inheritdoc/>
    public DehydratedState Dehydrate(DehydrateOptions? options)
    {
        ThrowIfDisposed();
        // Resolve each property independently so that passing options with only
        // one property set doesn't discard client-default values for the others.
        // TanStack resolves per-property; the previous code used a single
        // `resolvedOptions = options ?? defaults` which lost partial overrides.
        var shouldDehydrateQuery = options?.ShouldDehydrateQuery
            ?? _defaultOptions?.Dehydrate?.ShouldDehydrateQuery
            // Default: only succeeded, non-placeholder queries.
            // Excluding placeholders prevents circular dehydrate→hydrate→dehydrate
            // chains — placeholders hold object? data that would produce a degraded
            // dehydrated entry if re-dehydrated before upgrade.
            ?? (query => query.CurrentStatus == QueryStatus.Succeeded && !query.IsHydratedPlaceholder);

        var shouldDehydrateMutation = options?.ShouldDehydrateMutation
            ?? _defaultOptions?.Dehydrate?.ShouldDehydrateMutation
            ?? (mutation => mutation.CurrentIsPaused);

        var serializeData = options?.SerializeData
            ?? _defaultOptions?.Dehydrate?.SerializeData;

        var shouldRedactErrors = options?.ShouldRedactErrors
            ?? _defaultOptions?.Dehydrate?.ShouldRedactErrors
            ?? (_ => true);

        var now = TimeProvider.GetUtcNowMs();

        var queries = new List<DehydratedQuery>();
        foreach (var query in _queryCache.GetAll())
        {
            if (shouldDehydrateQuery(query))
            {
                queries.Add(TransformDehydratedQuery(
                    query.Dehydrate(now), serializeData, shouldRedactErrors));
            }
        }

        var mutations = new List<DehydratedMutation>();
        foreach (var mutation in _mutationCache.GetAll())
        {
            if (shouldDehydrateMutation(mutation))
            {
                mutations.Add(DehydrateMutation(mutation));
            }
        }

        return new DehydratedState { Queries = queries, Mutations = mutations };
    }

    private static DehydratedQuery TransformDehydratedQuery(
        DehydratedQuery original,
        Func<object?, object?>? serializeData,
        Func<Exception, bool> shouldRedactErrors)
    {
        var state = original.State;

        var data = state.Data is not null && serializeData is not null
            ? serializeData(state.Data) : state.Data;

        var error = state.Error is not null && shouldRedactErrors(state.Error)
            ? new RedactedException() : state.Error;

        var failureReason = state.FetchFailureReason is not null && shouldRedactErrors(state.FetchFailureReason)
            ? new RedactedException() : state.FetchFailureReason;

        // Short-circuit: nothing changed, return original to avoid allocation.
        if (ReferenceEquals(data, state.Data)
            && ReferenceEquals(error, state.Error)
            && ReferenceEquals(failureReason, state.FetchFailureReason))
        {
            return original;
        }

        return new DehydratedQuery
        {
            QueryHash = original.QueryHash,
            QueryKey = original.QueryKey,
            State = new DehydratedQueryState
            {
                Data = data,
                DataUpdateCount = state.DataUpdateCount,
                DataUpdatedAt = state.DataUpdatedAt,
                Error = error,
                ErrorUpdateCount = state.ErrorUpdateCount,
                ErrorUpdatedAt = state.ErrorUpdatedAt,
                FetchFailureCount = state.FetchFailureCount,
                FetchFailureReason = failureReason,
                FetchMeta = state.FetchMeta,
                IsInvalidated = state.IsInvalidated,
                Status = state.Status,
                FetchStatus = state.FetchStatus,
            },
            Meta = original.Meta,
            DehydratedAt = original.DehydratedAt,
        };
    }

    /// <summary>
    /// Delegates to <see cref="Mutation.Dehydrate()"/> which has access to
    /// the full generic state including Data, Variables, and Context.
    /// </summary>
    private static DehydratedMutation DehydrateMutation(Mutation mutation) => mutation.Dehydrate();

    /// <summary>
    /// Applies dehydrated state to this client's cache. New queries are created
    /// as <c>Query&lt;object&gt;</c> placeholders that are upgraded to properly-typed
    /// queries when an observer subscribes. Existing queries are updated only when
    /// the dehydrated data is newer.
    /// </summary>
    /// <remarks>
    /// Wrap hydration in application startup or state-transfer code. All cache
    /// mutations are batched via <see cref="NotifyManager"/> to coalesce
    /// observer notifications.
    /// </remarks>
    /// <inheritdoc cref="Hydrate(DehydratedState?, HydrateOptions?)"/>
    public void Hydrate(DehydratedState? state) => Hydrate(state, null);

    /// <inheritdoc/>
    public void Hydrate(DehydratedState? state, HydrateOptions? options)
    {
        ThrowIfDisposed();
        if (state is null)
        {
            return;
        }

        // Resolve each property independently (same pattern as Dehydrate).
        var queryDefaults = options?.Queries ?? _defaultOptions?.Hydrate?.Queries;
        var mutationDefaults = options?.Mutations ?? _defaultOptions?.Hydrate?.Mutations;
        var deserializeData = options?.DeserializeData ?? _defaultOptions?.Hydrate?.DeserializeData;

        NotifyManager.Batch(() =>
        {
            // ── Mutations ────────────────────────────────────────────
            foreach (var dehydratedMutation in state.Mutations)
            {
                var mutationState = new MutationState<object, object, object?>
                {
                    Data = dehydratedMutation.State.Data,
                    Error = dehydratedMutation.State.Error,
                    FailureCount = dehydratedMutation.State.FailureCount,
                    FailureReason = dehydratedMutation.State.FailureReason,
                    IsPaused = dehydratedMutation.State.IsPaused,
                    Status = dehydratedMutation.State.Status,
                    Variables = dehydratedMutation.State.Variables,
                    SubmittedAt = dehydratedMutation.State.SubmittedAt,
                    Context = dehydratedMutation.State.Context,
                };

                var mutationOptions = new MutationOptions<object, Exception, object, object?>
                {
                    MutationKey = dehydratedMutation.MutationKey,
                    Meta = dehydratedMutation.Meta,
                    Scope = dehydratedMutation.Scope,
                    GcTime = mutationDefaults?.GcTime ?? QueryTimeDefaults.GcTime,
                    Retry = mutationDefaults?.Retry,
                    RetryDelay = mutationDefaults?.RetryDelay,
                    MutationFn = mutationDefaults?.MutationFn,
                };

                _mutationCache.GetOrCreate(this, mutationOptions, mutationState);
            }

            // ── Queries ──────────────────────────────────────────────
            var queryCache = _queryCache;

            foreach (var dehydratedQuery in state.Queries)
            {
                var dehydratedData = dehydratedQuery.State.Data;
                if (dehydratedData is not null && deserializeData is not null)
                {
                    dehydratedData = deserializeData(dehydratedData);
                }

                var existing = queryCache.GetByHash(dehydratedQuery.QueryHash);

                if (existing is not null)
                {
                    // Only overwrite when the dehydrated data is newer.
                    if (dehydratedQuery.State.DataUpdatedAt > existing.DataUpdatedAt)
                    {
                        var transformedState = ReferenceEquals(dehydratedData, dehydratedQuery.State.Data)
                            ? dehydratedQuery.State
                            : new DehydratedQueryState
                            {
                                Data = dehydratedData,
                                DataUpdateCount = dehydratedQuery.State.DataUpdateCount,
                                DataUpdatedAt = dehydratedQuery.State.DataUpdatedAt,
                                Error = dehydratedQuery.State.Error,
                                ErrorUpdateCount = dehydratedQuery.State.ErrorUpdateCount,
                                ErrorUpdatedAt = dehydratedQuery.State.ErrorUpdatedAt,
                                FetchFailureCount = dehydratedQuery.State.FetchFailureCount,
                                FetchFailureReason = dehydratedQuery.State.FetchFailureReason,
                                FetchMeta = dehydratedQuery.State.FetchMeta,
                                IsInvalidated = dehydratedQuery.State.IsInvalidated,
                                Status = dehydratedQuery.State.Status,
                                FetchStatus = dehydratedQuery.State.FetchStatus,
                            };
                        existing.ApplyDehydratedState(transformedState);
                    }
                    // else: existing data is same age or newer — skip
                }
                else
                {
                    // New query: create a Query<object> placeholder.
                    // Normalize status: if data is present, ensure Succeeded.
                    var status = dehydratedData is not null
                        ? QueryStatus.Succeeded
                        : dehydratedQuery.State.Status;

                    var typedState = new QueryState<object>
                    {
                        Data = dehydratedData,
                        DataUpdateCount = dehydratedQuery.State.DataUpdateCount,
                        DataUpdatedAt = dehydratedQuery.State.DataUpdatedAt,
                        Error = dehydratedQuery.State.Error,
                        ErrorUpdateCount = dehydratedQuery.State.ErrorUpdateCount,
                        ErrorUpdatedAt = dehydratedQuery.State.ErrorUpdatedAt,
                        FetchFailureCount = dehydratedQuery.State.FetchFailureCount,
                        FetchFailureReason = dehydratedQuery.State.FetchFailureReason,
                        FetchMeta = dehydratedQuery.State.FetchMeta,
                        IsInvalidated = dehydratedQuery.State.IsInvalidated,
                        Status = status,
                        FetchStatus = FetchStatus.Idle,
                    };

                    var gcTime = queryDefaults?.GcTime
                        ?? _defaultOptions?.GcTime
                        ?? QueryTimeDefaults.GcTime;

                    var queryOptions = new QueryConfiguration<object>
                    {
                        QueryKey = dehydratedQuery.QueryKey,
                        QueryHash = dehydratedQuery.QueryHash,
                        GcTime = gcTime,
                        Retry = queryDefaults?.Retry,
                        RetryDelay = queryDefaults?.RetryDelay,
                        Meta = dehydratedQuery.Meta,
                    };

                    var config = new QueryConfig<object>
                    {
                        Client = this,
                        QueryKey = dehydratedQuery.QueryKey,
                        QueryHash = dehydratedQuery.QueryHash,
                        Options = DefaultQueryOptions(queryOptions),
                        State = typedState,
                        DefaultOptions = GetQueryDefaults<object>(dehydratedQuery.QueryKey),
                        Metrics = Metrics,
                        IsHydratedPlaceholder = true,
                    };

                    var placeholderQuery = new Query<object>(config);
                    queryCache.Add(placeholderQuery);
                }
            }
        });
    }
}
