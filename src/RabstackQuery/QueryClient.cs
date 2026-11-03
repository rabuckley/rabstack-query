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
    private readonly List<(QueryKey Key, QueryDefaults Defaults)> _queryDefaults = [];
    private readonly List<(QueryKey Key, MutationDefaults Defaults)> _mutationDefaults = [];
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
#pragma warning disable RS0030 // The single default clock; everything else reads it from this client.
        TimeProvider = timeProvider ?? TimeProvider.System;
#pragma warning restore RS0030
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
    /// Resumes paused mutations when the client is online.
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
    /// Asynchronously fetches a query. If the query exists in the cache and is
    /// not stale, returns the cached data without fetching. Otherwise fetches
    /// fresh data. Does not create a persistent observer subscription.
    /// </summary>
    /// <remarks>
    /// Options left <see langword="null"/> inherit from the key-prefix and client-wide
    /// <see cref="QueryDefaults"/>. When no layer sets <c>Retry</c>, it is 0 (no retries).
    /// Errors propagate to the caller.
    /// </remarks>
    public async Task<TData> FetchQueryAsync<TData>(
        FetchQueryOptions<TData> options)
    {
        ThrowIfDisposed();
        var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        var queryHash = hasher.HashQueryKey(options.QueryKey);
        _logger.FetchQuery(queryHash);

        var settings = ResolveFetchSettings(options.QueryKey, options.ToQueryDefaults());
        var queryOptions = new QueryConfiguration<TData>
        {
            QueryKey = options.QueryKey,
            QueryKeyHasher = hasher,
            Meta = options.Meta,
        };
        settings.CopyTo(queryOptions);

        var query = _queryCache.GetOrCreate<TData, TData>(this, queryOptions);
        query.SetQueryFn(options.QueryFn);

        // If the cache has fresh data, return it directly.
        // The ! operator is justified: Data is logically present when status is
        // Succeeded, but TData is unconstrained so the compiler can't narrow TData?.
        if (query.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, settings.StaleTime, state.IsInvalidated))
        {
            _logger.FetchQueryCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        Metrics.CacheMissTotal?.Add(1);

        await query.Fetch(queryOptions, meta: null, cancelRefetch: false);

        Debug.Assert(query.State is not null);
        return query.State.Data!;
    }

    /// <summary>
    /// Like <see cref="FetchQueryAsync{TData}"/> but swallows all exceptions.
    /// Useful for warming the cache in the background.
    /// </summary>
    public async Task PrefetchQueryAsync<TData>(
        FetchQueryOptions<TData> options)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.PrefetchQuery(queryHash);

        try
        {
            await FetchQueryAsync(options);
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
        FetchQueryOptions<TData> options)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.EnsureQueryData(queryHash);

        var query = _queryCache.Get<TData>(queryHash);
        var staleTime = ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults()).StaleTime;

        if (query?.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, staleTime, state.IsInvalidated))
        {
            _logger.EnsureQueryDataCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        // If InitialData is provided and there's no cached data, seed the cache
        // and return it immediately — optionally triggering a background refetch.
        if (options.InitialData is not null && query?.State?.Status is not QueryStatus.Succeeded)
        {
            SeedInitialData(query, options.QueryKey, queryHash, options.InitialData);

            if (options.RevalidateIfStale)
            {
                // Fire-and-forget background refetch — caller gets InitialData immediately.
                _ = FetchQueryAsync(options).ContinueWith(
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
            _ = FetchQueryAsync(options).ContinueWith(
                static t => { /* swallow — background revalidation is best-effort */ },
                TaskScheduler.Default);

            return staleState.Data!;
        }

        // Cache miss is recorded inside FetchQueryAsync when it proceeds to fetch.
        return await FetchQueryAsync(options);
    }

    private void SeedInitialData<TData>(Query<TData>? query, QueryKey queryKey, string queryHash, TData data)
    {
        if (query is not null)
        {
            query.SeedInitialData(data, TimeProvider.GetUtcNowMs());
            return;
        }

        _queryCache.GetOrCreate<TData, TData>(this, new QueryConfiguration<TData>
        {
            QueryKey = queryKey,
            QueryHash = queryHash,
            InitialData = data,
        });
    }

    // ── Imperative infinite query fetch methods ─────────────────────────

    /// <summary>
    /// Asynchronously fetches an infinite query. If the query exists in the cache
    /// and is not stale, returns the cached data without fetching. Otherwise
    /// fetches the first page. Does not create a persistent observer subscription.
    /// </summary>
    /// <remarks>
    /// Options left <see langword="null"/> inherit from the key-prefix and client-wide
    /// <see cref="QueryDefaults"/>. When no layer sets <c>Retry</c>, it is 0 (no retries).
    /// Errors propagate to the caller.
    /// </remarks>
    public async Task<InfiniteData<TData, TPageParam>> FetchInfiniteQueryAsync<TData, TPageParam>(
        FetchInfiniteQueryOptions<TData, TPageParam> options)
    {
        ThrowIfDisposed();
        var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        var queryHash = hasher.HashQueryKey(options.QueryKey);
        _logger.FetchInfiniteQuery(queryHash);

        var settings = ResolveFetchSettings(options.QueryKey, options.ToQueryDefaults());
        var queryOptions = new QueryConfiguration<InfiniteData<TData, TPageParam>>
        {
            QueryKey = options.QueryKey,
            QueryKeyHasher = hasher,
        };
        settings.CopyTo(queryOptions);

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
            && !IsDataStale(state.DataUpdatedAt, settings.StaleTime, state.IsInvalidated))
        {
            _logger.FetchInfiniteQueryCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        Metrics.CacheMissTotal?.Add(1);

        await query.Fetch(queryOptions, meta: null, cancelRefetch: false);

        Debug.Assert(query.State is not null);
        return query.State.Data!;
    }

    /// <summary>
    /// Like <see cref="FetchInfiniteQueryAsync{TData,TPageParam}"/> but swallows all
    /// exceptions. Useful for warming the cache in the background.
    /// </summary>
    public async Task PrefetchInfiniteQueryAsync<TData, TPageParam>(
        FetchInfiniteQueryOptions<TData, TPageParam> options)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.PrefetchInfiniteQuery(queryHash);

        try
        {
            await FetchInfiniteQueryAsync(options);
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
        FetchInfiniteQueryOptions<TData, TPageParam> options)
    {
        ThrowIfDisposed();
        var queryHash = (options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance).HashQueryKey(options.QueryKey);
        _logger.EnsureInfiniteQueryData(queryHash);

        var query = _queryCache.Get<InfiniteData<TData, TPageParam>>(queryHash);
        var staleTime = ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults()).StaleTime;

        if (query?.State is { Status: QueryStatus.Succeeded } state
            && !IsDataStale(state.DataUpdatedAt, staleTime, state.IsInvalidated))
        {
            _logger.EnsureInfiniteQueryDataCacheHit(queryHash);
            Metrics.CacheHitTotal?.Add(1);
            return state.Data!;
        }

        // If InitialData is provided and there's no cached data, seed the cache
        // and return it immediately — optionally triggering a background refetch.
        if (options.InitialData is not null && query?.State?.Status is not QueryStatus.Succeeded)
        {
            SeedInitialData(query, options.QueryKey, queryHash, options.InitialData);

            if (options.RevalidateIfStale)
            {
                // Fire-and-forget background refetch — caller gets InitialData immediately.
                _ = FetchInfiniteQueryAsync(options).ContinueWith(
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
            _ = FetchInfiniteQueryAsync(options).ContinueWith(
                static t => { /* swallow — background revalidation is best-effort */ },
                TaskScheduler.Default);

            return staleState.Data!;
        }

        // Cache miss is recorded inside FetchInfiniteQueryAsync when it proceeds to fetch.
        return await FetchInfiniteQueryAsync(options);
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
    /// Gets or sets the client-wide default options. A change applies to queries and
    /// mutations whose options are defaulted afterwards; existing cache entries keep theirs.
    /// </summary>
    public QueryClientDefaultOptions? DefaultOptions
    {
        get { ThrowIfDisposed(); return _defaultOptions; }
        set { ThrowIfDisposed(); _defaultOptions = value; }
    }

    /// <summary>
    /// Registers defaults for every query whose key starts with <paramref name="queryKey"/>.
    /// </summary>
    /// <remarks>
    /// Registering again for the same key replaces the earlier registration in place. When
    /// several registrations match a query they apply in registration order, so register
    /// broader prefixes first. Per-query options override these defaults, and these override
    /// <see cref="QueryClientDefaultOptions.Queries"/>.
    /// </remarks>
    /// <param name="queryKey">The key prefix that query keys are matched against.</param>
    /// <param name="defaults">The defaults to apply to matching queries.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="queryKey"/> or <paramref name="defaults"/> is <see langword="null"/>.
    /// </exception>
    public void SetQueryDefaults(QueryKey queryKey, QueryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ArgumentNullException.ThrowIfNull(defaults);
        ThrowIfDisposed();

        // Replace in place so re-registering a key does not change its precedence.
        var index = _queryDefaults.FindIndex(d => QueryKeyMatcher.ExactMatchKey(d.Key, queryKey));
        if (index >= 0)
        {
            _queryDefaults[index] = (queryKey, defaults);
        }
        else
        {
            _queryDefaults.Add((queryKey, defaults));
        }
    }

    /// <summary>
    /// Returns the key-prefix defaults that apply to <paramref name="queryKey"/>, merged in
    /// registration order. Client-wide defaults are not included.
    /// </summary>
    /// <param name="queryKey">The query key to look up registered defaults for.</param>
    /// <returns>
    /// The merged defaults, with every property <see langword="null"/> when no registration matches.
    /// </returns>
    public QueryDefaults GetQueryDefaults(QueryKey queryKey)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ThrowIfDisposed();
        return OptionsDefaulting.MergeQueryLayers(null, _queryDefaults, queryKey, perQuery: null);
    }

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with defaultable options resolved
    /// through client-wide defaults, key-prefix defaults and framework fallbacks.
    /// </summary>
    /// <remarks>
    /// Per-query values override key-prefix defaults, which override client-wide defaults.
    /// A <see cref="SkipToken"/> query function sets
    /// <see cref="QueryObserverOptions{TData,TQueryData}.Enabled"/> to <see langword="false"/>.
    /// </remarks>
    public QueryObserverOptions<TData, TQueryData> DefaultQueryOptions<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        if (options.QueryKey is null)
        {
            throw new ArgumentException("QueryKey must be provided.", nameof(options));
        }

        var settings = ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults());

        return options with
        {
            Enabled = options.Enabled && !SkipToken.IsSkipToken(options.QueryFn),
            StaleTime = settings.StaleTime,
            GcTime = settings.GcTime,
            Retry = settings.Retry,
            RetryDelay = settings.RetryDelay,
            RefetchOnWindowFocus = settings.RefetchOnWindowFocus,
            RefetchOnReconnect = settings.RefetchOnReconnect,
            RefetchOnMount = settings.RefetchOnMount,
            NetworkMode = settings.NetworkMode,
        };
    }

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with <c>GcTime</c>, <c>Retry</c>,
    /// <c>RetryDelay</c> and <c>NetworkMode</c> resolved through client-wide defaults,
    /// key-prefix defaults and framework fallbacks.
    /// </summary>
    /// <remarks>
    /// Per-query values override key-prefix defaults, which override client-wide defaults.
    /// Options without a
    /// <see cref="QueryConfiguration{TData}.QueryKey"/> are returned unchanged.
    /// </remarks>
    public QueryConfiguration<TData> DefaultQueryOptions<TData>(QueryConfiguration<TData> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        if (options.QueryKey is null)
        {
            return options;
        }

        var settings = ResolveQuerySettings(options.QueryKey, options.ToQueryDefaults());

        var result = new QueryConfiguration<TData>
        {
            QueryKey = options.QueryKey,
            QueryHash = options.QueryHash,
            QueryKeyHasher = options.QueryKeyHasher,
            // InitialData is set conditionally below to preserve HasInitialData semantics
            InitialDataFactory = options.InitialDataFactory,
            InitialDataUpdatedAt = options.InitialDataUpdatedAt,
            InitialDataUpdatedAtFactory = options.InitialDataUpdatedAtFactory,
            Meta = options.Meta,
        };

        // Only propagate InitialData when the source explicitly set it. Assigning
        // the property (even to default) sets HasInitialData=true, which would cause
        // value-type queries to start as Succeeded instead of Pending.
        if (options.HasInitialData)
        {
            result.InitialData = options.InitialData;
        }

        settings.CopyTo(result);
        return result;
    }

    internal ResolvedQuerySettings ResolveQuerySettings(QueryKey queryKey, QueryDefaults perQuery)
        => ResolvedQuerySettings.From(
            OptionsDefaulting.MergeQueryLayers(_defaultOptions?.Queries, _queryDefaults, queryKey, perQuery));

    private ResolvedQuerySettings ResolveFetchSettings(QueryKey queryKey, QueryDefaults perQuery)
    {
        var merged = OptionsDefaulting.MergeQueryLayers(_defaultOptions?.Queries, _queryDefaults, queryKey, perQuery);

        return ResolvedQuerySettings.From(merged with { Retry = merged.Retry ?? 0 });
    }

    // ── Mutation defaults ───────────────────────────────────────────────

    /// <summary>
    /// Registers defaults for every mutation whose key starts with <paramref name="mutationKey"/>.
    /// </summary>
    /// <remarks>
    /// Registering again for the same key replaces the earlier registration in place. When
    /// several registrations match a mutation they apply in registration order. Per-mutation
    /// options override these defaults, and these override
    /// <see cref="QueryClientDefaultOptions.Mutations"/>.
    /// </remarks>
    /// <param name="mutationKey">The key prefix that mutation keys are matched against.</param>
    /// <param name="defaults">The defaults to apply to matching mutations.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mutationKey"/> or <paramref name="defaults"/> is <see langword="null"/>.
    /// </exception>
    public void SetMutationDefaults(QueryKey mutationKey, MutationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(mutationKey);
        ArgumentNullException.ThrowIfNull(defaults);
        ThrowIfDisposed();

        var index = _mutationDefaults.FindIndex(d => QueryKeyMatcher.ExactMatchKey(d.Key, mutationKey));
        if (index >= 0)
        {
            _mutationDefaults[index] = (mutationKey, defaults);
        }
        else
        {
            _mutationDefaults.Add((mutationKey, defaults));
        }
    }

    /// <summary>
    /// Returns the key-prefix defaults that apply to <paramref name="mutationKey"/>, merged in
    /// registration order. Client-wide defaults are not included.
    /// </summary>
    /// <param name="mutationKey">The mutation key to look up registered defaults for.</param>
    /// <returns>
    /// The merged defaults, with every property <see langword="null"/> when no registration matches.
    /// </returns>
    public MutationDefaults GetMutationDefaults(QueryKey mutationKey)
    {
        ArgumentNullException.ThrowIfNull(mutationKey);
        ThrowIfDisposed();
        return OptionsDefaulting.MergeMutationLayers(null, _mutationDefaults, mutationKey, perMutation: null);
    }

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with <c>Retry</c>, <c>RetryDelay</c>,
    /// <c>GcTime</c> and <c>NetworkMode</c> resolved through client-wide defaults, key-prefix
    /// defaults and framework fallbacks. Options already defaulted are returned unchanged.
    /// </summary>
    public MutationOptions<TData, TError, TVariables, TOnMutateResult>
        DefaultMutationOptions<TData, TError, TVariables, TOnMutateResult>(
            MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
        where TError : Exception
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        if (options.Defaulted)
        {
            return options;
        }

        var merged = OptionsDefaulting.MergeMutationLayers(
            _defaultOptions?.Mutations, _mutationDefaults, options.MutationKey, options.ToMutationDefaults());

        return options with
        {
            Retry = merged.Retry ?? OptionsDefaulting.FallbackMutationRetry,
            RetryDelay = merged.RetryDelay,
            GcTime = merged.GcTime ?? OptionsDefaulting.FallbackGcTime,
            NetworkMode = merged.NetworkMode ?? OptionsDefaulting.FallbackNetworkMode,
            Defaulted = true,
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
        InvalidateQueryFilters? filters = null)
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
                });
        });

        await refetchTask;
    }

    /// <summary>Convenience overload that does a prefix match on the key.</summary>
    public Task InvalidateQueriesAsync(QueryKey queryKey)
    {
        ThrowIfDisposed();
        return InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = queryKey });
    }

    /// <summary>
    /// Refetches all queries.
    /// </summary>
    public Task RefetchQueriesAsync()
        => RefetchQueriesAsync(filters: null);

    /// <summary>
    /// Refetches all queries matching the given filters.
    /// This immediately triggers a fetch regardless of stale status.
    /// </summary>
    public Task RefetchQueriesAsync(QueryFilters? filters)
    {
        ThrowIfDisposed();
        return RefetchQueriesAsync(filters, options: null);
    }

    /// <summary>
    /// Asynchronously refetches matching queries, excluding disabled and static queries.
    /// Paused fetches continue in the background without delaying completion.
    /// </summary>
    public async Task RefetchQueriesAsync(QueryFilters? filters, RefetchOptions? options)
    {
        ThrowIfDisposed();
        _logger.RefetchQueries();
        var cancelRefetch = options?.CancelRefetch ?? true;

        var tasks = _queryCache.FindAll(filters)
            .Where(q => !q.IsDisabled() && !q.IsStatic())
            .Select(q =>
            {
                var fetchTask = q.Fetch(cancelRefetch);

                // As in queryClient.ts QueryClient#refetchQueries, do not await paused fetches,
                // which wait for connectivity. OnPause runs before Fetch's first suspension,
                // so the paused status is available here.
                if (q.CurrentFetchStatus is FetchStatus.Paused)
                {
                    return Task.CompletedTask;
                }

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
    public async Task RefetchQueriesAsync(QueryKey queryKey)
    {
        ThrowIfDisposed();
        await RefetchQueriesAsync(new QueryFilters { QueryKey = queryKey });
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
    /// </summary>
    /// <typeparam name="TData">The type of data to set.</typeparam>
    /// <param name="queryKey">The query key to update.</param>
    /// <param name="data">The data to set in the cache.</param>
    /// <example>
    /// <code>
    /// var addTodoMutation = client.UseMutation(new MutationOptions&lt;Todo, Todo&gt;
    /// {
    ///     MutationFn = async (newTodo, context) => await api.CreateTodo(newTodo),
    ///     OnMutate = async (newTodo, context) =>
    ///     {
    ///         var todos = client.GetQueryData&lt;List&lt;Todo&gt;&gt;(["todos"]) ?? [];
    ///         client.SetQueryData(["todos"], todos.Append(newTodo).ToList());
    ///         return null;
    ///     },
    ///     OnError = async (error, variables, _, context) =>
    ///     {
    ///         await client.InvalidateQueriesAsync(["todos"]);
    ///     },
    /// });
    /// </code>
    /// </example>
    // TODO: Unlike query.ts Query#setData, SetQueryData bypasses structural sharing.
    // Sharing runs in QueryObserver.UpdateResult, so direct cache reads do not benefit.
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

            query.SetManualData(data, now);
        }
        else
        {
            // TanStack's setQueryData is a no-op when data is undefined for a
            // nonexistent query — it does not create a cache entry with null data.
            if (data is null)
            {
                return;
            }

            // Leave InitialData unset so Reset() clears data written by SetQueryData.
            // Assigning it here would make that data the reset target.
            var options = new QueryConfiguration<TData>
            {
                QueryKey = queryKey,
                QueryHash = queryHash,
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
    public Task<TData> FetchQueryAsync<TData>(QueryOptions<TData> options)
    {
        ThrowIfDisposed();
        return FetchQueryAsync(options.ToFetchQueryOptions());
    }

    /// <summary>
    /// Prefetches a query using the key and function from <paramref name="options"/>.
    /// Errors are swallowed — useful for warming the cache in the background.
    /// </summary>
    public Task PrefetchQueryAsync<TData>(QueryOptions<TData> options)
    {
        ThrowIfDisposed();
        return PrefetchQueryAsync(options.ToFetchQueryOptions());
    }

    /// <summary>
    /// Returns cached data if fresh, otherwise fetches using <paramref name="options"/>.
    /// </summary>
    public Task<TData> EnsureQueryDataAsync<TData>(QueryOptions<TData> options)
    {
        ThrowIfDisposed();
        return EnsureQueryDataAsync(options.ToFetchQueryOptions());
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
    /// <param name="filters">The queries to cancel; <see langword="null"/> matches all queries.</param>
    /// <param name="options">How the cancellation affects query state; <see langword="null"/> reverts it.</param>
    /// <remarks>
    /// The returned task completes once the matching fetches have settled, so a result from a cancelled
    /// fetch cannot overwrite data written afterwards. It does not wait for a query function that ignores
    /// its token; that function's eventual result is discarded.
    /// </remarks>
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
                    GcTime = mutationDefaults?.GcTime,
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

                    var queryOptions = new QueryConfiguration<object>
                    {
                        QueryKey = dehydratedQuery.QueryKey,
                        QueryHash = dehydratedQuery.QueryHash,
                        GcTime = queryDefaults?.GcTime,
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
                        Metrics = Metrics,
                        IsHydratedPlaceholder = true,
                    };

                    var placeholderQuery = new Query<object>(config);
                    if (!ReferenceEquals(queryCache.Add(placeholderQuery), placeholderQuery))
                    {
                        // The discarded placeholder still owns its constructor's GC timer.
                        placeholderQuery.Destroy();
                    }
                }
            }
        });
    }
}
