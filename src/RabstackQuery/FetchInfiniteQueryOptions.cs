namespace RabstackQuery;

/// <summary>
/// Options for imperative infinite query fetch methods:
/// <see cref="QueryClient.FetchInfiniteQueryAsync{TData,TPageParam}"/>,
/// <see cref="QueryClient.PrefetchInfiniteQueryAsync{TData,TPageParam}"/>, and
/// <see cref="QueryClient.EnsureInfiniteQueryDataAsync{TData,TPageParam}"/>.
/// </summary>
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
    /// means unlimited.
    /// </summary>
    public int MaxPages { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.StaleTime"/>
    public TimeSpan? StaleTime { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.GcTime"/>
    public TimeSpan? GcTime { get; init; }

    /// <inheritdoc cref="FetchQueryOptions{TData}.Retry"/>
    public int? Retry { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Initial data to use if the cache is empty. When set,
    /// <see cref="QueryClient.EnsureInfiniteQueryDataAsync{TData,TPageParam}"/> returns
    /// this data immediately without fetching, and seeds the cache for future callers.
    /// </summary>
    public InfiniteData<TData, TPageParam>? InitialData { get; init; }

    /// <summary>
    /// When true, <see cref="QueryClient.EnsureInfiniteQueryDataAsync{TData,TPageParam}"/>
    /// triggers a background refetch if the cached (or initial) data is stale, while
    /// still returning the existing data immediately.
    /// </summary>
    public bool RevalidateIfStale { get; init; }

    internal QueryDefaults ToQueryDefaults() => new()
    {
        StaleTime = StaleTime,
        GcTime = GcTime,
        Retry = Retry,
    };
}
