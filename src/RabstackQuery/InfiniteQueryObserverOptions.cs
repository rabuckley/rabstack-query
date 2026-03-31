namespace RabstackQuery;

/// <summary>
/// Options for <see cref="InfiniteQueryObserver{TData,TPageParam}"/>. Composes
/// standard observer fields with infinite-query-specific configuration (page
/// param delegates, initial page param, max pages). Avoids duplicating the
/// full <see cref="QueryObserverOptions{TData,TQueryData}"/> surface.
/// </summary>
public sealed class InfiniteQueryObserverOptions<TData, TPageParam>
{
    // ── Infinite-query-specific ─────────────────────────────────────────

    /// <summary>
    /// The query function that fetches a single page. Receives the page param,
    /// direction, and cancellation token via <see cref="InfiniteQueryFunctionContext{TPageParam}"/>.
    /// </summary>
    public required Func<InfiniteQueryFunctionContext<TPageParam>, Task<TData>> QueryFn { get; init; }

    /// <summary>
    /// The page param used to fetch the first page.
    /// </summary>
    public required TPageParam InitialPageParam { get; init; }

    /// <summary>
    /// Determines the page param for the next page. Return
    /// <see cref="PageParamResult{T}.None"/> to indicate no more pages.
    /// </summary>
    public required Func<PageParamContext<TData, TPageParam>, PageParamResult<TPageParam>> GetNextPageParam { get; init; }

    /// <summary>
    /// Determines the page param for the previous page. Return
    /// <see cref="PageParamResult{T}.None"/> to indicate no more pages.
    /// When <c>null</c>, backward fetching is not supported.
    /// </summary>
    public Func<PageParamContext<TData, TPageParam>, PageParamResult<TPageParam>>? GetPreviousPageParam { get; init; }

    /// <summary>
    /// Maximum number of pages to keep in the cache. When exceeded, pages are
    /// dropped from the opposite end of the fetch direction. <c>0</c> (default)
    /// means unlimited. Mirrors TanStack's <c>maxPages</c> option.
    /// </summary>
    public int MaxPages { get; init; }

    /// <summary>
    /// Optional transform applied to the <see cref="InfiniteData{TData,TPageParam}"/>
    /// before exposing it in results. Unlike the standard observer's
    /// <see cref="QueryObserverOptions{TData,TQueryData}.Select"/> which changes the
    /// data type, this preserves the <c>InfiniteData</c> wrapper.
    /// </summary>
    public Func<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>>? Select { get; init; }

    // ── Standard observer options (composed, not duplicated) ────────────

    /// <summary>The query key for cache identity.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.Enabled"/>
    public bool Enabled { get; init; } = true;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.StaleTime"/>
    public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.GcTime"/>
    public TimeSpan GcTime { get; init; } = QueryTimeDefaults.GcTime;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RefetchOnWindowFocus"/>
    public RefetchOnBehavior RefetchOnWindowFocus { get; init; } = RefetchOnBehavior.WhenStale;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RefetchOnReconnect"/>
    public RefetchOnBehavior RefetchOnReconnect { get; init; } = RefetchOnBehavior.WhenStale;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RefetchOnMount"/>
    public RefetchOnBehavior RefetchOnMount { get; init; } = RefetchOnBehavior.WhenStale;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RefetchInterval"/>
    public TimeSpan RefetchInterval { get; init; } = TimeSpan.Zero;

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RefetchIntervalInBackground"/>
    public bool RefetchIntervalInBackground { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.NotifyOnChangeProps"/>
    public IReadOnlySet<string>? NotifyOnChangeProps { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.QueryKeyHasher"/>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    // Retry is intentionally omitted. Retry configuration is controlled at the
    // query level via QueryConfiguration<TData>.Retry (defaulting to 3). There is no
    // per-observer retry override — the inner QueryObserver doesn't forward it.

    /// <summary>
    /// Builds the inner <see cref="QueryObserverOptions{TData,TQueryData}"/> used by
    /// the composed <see cref="QueryObserver{TData,TQueryData}"/>. The
    /// <paramref name="queryFn"/> is the function built by
    /// <see cref="InfiniteQueryBehavior"/> that handles page fetching logic.
    /// </summary>
    internal QueryObserverOptions<InfiniteData<TData, TPageParam>> ToInnerOptions(
        Func<QueryFunctionContext, Task<InfiniteData<TData, TPageParam>>> queryFn)
    {
        return new QueryObserverOptions<InfiniteData<TData, TPageParam>>
        {
            QueryKey = QueryKey,
            QueryFn = queryFn,
            Enabled = Enabled,
            StaleTime = StaleTime,
            GcTime = GcTime,
            RefetchOnWindowFocus = RefetchOnWindowFocus,
            RefetchOnReconnect = RefetchOnReconnect,
            RefetchOnMount = RefetchOnMount,
            RefetchInterval = RefetchInterval,
            RefetchIntervalInBackground = RefetchIntervalInBackground,
            NetworkMode = NetworkMode,
            NotifyOnChangeProps = NotifyOnChangeProps,
            QueryKeyHasher = QueryKeyHasher,
        };
    }
}
