namespace RabstackQuery;

/// <summary>
/// Options for imperative infinite query fetch methods:
/// <see cref="QueryClient.FetchInfiniteQueryAsync{TData,TPageParam}"/>,
/// <see cref="QueryClient.PrefetchInfiniteQueryAsync{TData,TPageParam}"/>, and
/// <see cref="QueryClient.EnsureInfiniteQueryDataAsync{TData,TPageParam}"/>.
/// </summary>
[Obsolete("Use InfiniteQueryExecuteOptions<TData, TPageParam> with InfiniteQueryAsync instead. This type will be removed in the next major version.")]
public sealed class FetchInfiniteQueryOptions<TData, TPageParam>
{
    /// <summary>Query key to fetch.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <summary>
    /// The query function that fetches a single page. Receives the page param,
    /// direction, and cancellation token via <see cref="InfiniteQueryFunctionContext{TPageParam}"/>.
    /// </summary>
    public required Func<InfiniteQueryFunctionContext<TPageParam>, Task<TData>> QueryFn { get; init; }

    /// <summary>The page param used to fetch the first page.</summary>
    public required TPageParam InitialPageParam { get; init; }

    /// <inheritdoc cref="InfiniteQueryExecuteOptions{TData,TPageParam}.GetNextPageParam"/>
    public Func<PageParamContext<TData, TPageParam>, PageParamResult<TPageParam>>? GetNextPageParam { get; init; }

    /// <inheritdoc cref="InfiniteQueryExecuteOptions{TData,TPageParam}.Pages"/>
    public int? Pages { get; init; }

    /// <summary>
    /// Maximum number of pages to keep in the cache. When exceeded, pages are
    /// dropped from the opposite end of the fetch direction. <c>0</c> (default)
    /// means unlimited.
    /// </summary>
    public int MaxPages { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.StaleTime"/>
    public TimeSpan? StaleTime { get; init; }

    /// <inheritdoc cref="InfiniteQueryExecuteOptions{TData,TPageParam}.StaleTimeFn"/>
    public Func<Query<InfiniteData<TData, TPageParam>>, TimeSpan>? StaleTimeFn { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.GcTime"/>
    public TimeSpan? GcTime { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.Retry"/>
    public int? Retry { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.RetryDelay"/>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Gets or initializes the data that seeds a query with no data. Seeded data counts as cached.
    /// </summary>
    public InfiniteData<TData, TPageParam>? InitialData { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.InitialDataUpdatedAt"/>
    public long? InitialDataUpdatedAt { get; init; }

    /// <summary>
    /// When true, <see cref="QueryClient.EnsureInfiniteQueryDataAsync{TData,TPageParam}"/>
    /// triggers a background refetch if the cached (or initial) data is stale, while
    /// still returning the existing data immediately.
    /// </summary>
    public bool RevalidateIfStale { get; init; }

    internal InfiniteQueryExecuteOptions<TData, TPageParam> ToInfiniteQueryExecuteOptions() => new()
    {
        QueryKey = QueryKey,
        QueryFn = QueryFn,
        InitialPageParam = InitialPageParam,
        GetNextPageParam = GetNextPageParam,
        Pages = Pages,
        MaxPages = MaxPages,
        StaleTime = StaleTime,
        StaleTimeFn = StaleTimeFn,
        GcTime = GcTime,
        Retry = Retry,
        RetryDelay = RetryDelay,
        NetworkMode = NetworkMode,
        Meta = Meta,
        QueryKeyHasher = QueryKeyHasher,
        InitialData = InitialData,
        InitialDataUpdatedAt = InitialDataUpdatedAt,
    };
}
