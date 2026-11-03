namespace RabstackQuery;

/// <summary>
/// Represents the options for
/// <see cref="QueryClient.InfiniteQueryAsync{TData,TPageParam}(InfiniteQueryExecuteOptions{TData,TPageParam})"/>.
/// </summary>
/// <typeparam name="TData">The type of a single page.</typeparam>
/// <typeparam name="TPageParam">The type of the parameter that identifies a page.</typeparam>
/// <remarks>
/// Unset defaultable options inherit from key-prefix and client-wide <see cref="QueryDefaults"/>.
/// </remarks>
public sealed record InfiniteQueryExecuteOptions<TData, TPageParam>
{
    public required QueryKey QueryKey { get; init; }

    /// <summary>
    /// Gets or initializes the function that fetches a single page.
    /// </summary>
    /// <remarks>
    /// With <see cref="SkipToken.InfiniteQueryFn{TData,TPageParam}"/>, a call that needs to fetch
    /// fails instead.
    /// </remarks>
    public required Func<InfiniteQueryFunctionContext<TPageParam>, Task<TData>> QueryFn { get; init; }

    /// <summary>Gets or initializes the page parameter of the first page when the cache has no pages.</summary>
    public required TPageParam InitialPageParam { get; init; }

    /// <summary>
    /// Gets or initializes the function that determines the parameter of the next page, or
    /// <see cref="PageParamResult{T}.None"/> when there are no more pages.
    /// </summary>
    /// <value><see langword="null"/> treats the first page as the last. Required when <see cref="Pages"/> is set.</value>
    public Func<PageParamContext<TData, TPageParam>, PageParamResult<TPageParam>>? GetNextPageParam { get; init; }

    /// <summary>
    /// Gets or initializes the number of pages to fetch from the start, stopping early when
    /// <see cref="GetNextPageParam"/> returns <see cref="PageParamResult{T}.None"/>.
    /// </summary>
    /// <value>
    /// <see langword="null"/> fetches as many pages as are cached, or one page if none are.
    /// A value below 1 fetches one page.
    /// </value>
    public int? Pages { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of pages to keep. When exceeded, the earliest pages are dropped.
    /// </summary>
    /// <value>0, the default, keeps every page.</value>
    public int MaxPages { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.StaleTime"/>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>
    /// Gets or initializes a function that computes the stale duration from the cached query.
    /// When set, it takes precedence over <see cref="StaleTime"/>.
    /// </summary>
    public Func<Query<InfiniteData<TData, TPageParam>>, TimeSpan>? StaleTimeFn { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.GcTime"/>
    public TimeSpan? GcTime { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.Retry"/>
    public int? Retry { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.RetryDelay"/>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.QueryKeyHasher"/>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Gets or initializes the data used to seed a query with no data.
    /// </summary>
    /// <value><see langword="null"/> doesn't seed the query.</value>
    /// <remarks>Seeded data counts as cached, so it is refetched only if it is stale.</remarks>
    public InfiniteData<TData, TPageParam>? InitialData { get; init; }

    /// <inheritdoc cref="QueryExecuteOptions{TData}.InitialDataUpdatedAt"/>
    public long? InitialDataUpdatedAt { get; init; }

    internal QueryDefaults ToQueryDefaults() => new()
    {
        StaleTime = StaleTime,
        GcTime = GcTime,
        Retry = Retry,
        RetryDelay = RetryDelay,
        NetworkMode = NetworkMode,
    };
}
