namespace RabstackQuery;

/// <summary>
/// Options for imperative fetch methods: <see cref="QueryClient.FetchQueryAsync{TData}"/>,
/// <see cref="QueryClient.PrefetchQueryAsync{TData}"/>, and
/// <see cref="QueryClient.EnsureQueryDataAsync{TData}"/>.
/// </summary>
public sealed class FetchQueryOptions<TData>
{
    /// <summary>Query key to fetch.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <summary>Query function — called when the cache is empty or stale.</summary>
    public required Func<QueryFunctionContext, Task<TData>> QueryFn { get; init; }

    /// <summary>
    /// Data older than this duration is considered stale and will be refetched.
    /// Null means use the default (<see cref="TimeSpan.Zero"/> = always stale).
    /// </summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>Garbage collection duration. Null uses the default (5 min).</summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Number of retries. Null uses the default (0 for imperative fetch, matching
    /// TanStack's <c>fetchQuery</c> behavior where retry defaults to <c>false</c>).
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>Custom retry delay function. Receives failure count and exception.</summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public QueryMeta? Meta { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Initial data to use if the cache is empty. When set,
    /// <see cref="QueryClient.EnsureQueryDataAsync{TData}"/> returns this data
    /// immediately without fetching, and seeds the cache for future callers.
    /// </summary>
    public TData? InitialData { get; init; }

    /// <summary>
    /// When true, <see cref="QueryClient.EnsureQueryDataAsync{TData}"/> triggers
    /// a background refetch if the cached (or initial) data is stale, while still
    /// returning the existing data immediately. Mirrors TanStack's
    /// <c>revalidateIfStale</c> option on <c>ensureQueryData</c>.
    /// </summary>
    public bool RevalidateIfStale { get; init; }
}

/// <summary>
/// Static factory for creating <see cref="FetchQueryOptions{TData}"/> with type inference
/// from the query function delegate.
/// </summary>
public static class FetchQueryOptions
{
    /// <summary>
    /// Creates a <see cref="FetchQueryOptions{TData}"/> inferring <typeparamref name="TData"/>
    /// from the <paramref name="queryFn"/> delegate.
    /// </summary>
    public static FetchQueryOptions<TData> Create<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        return new FetchQueryOptions<TData> { QueryKey = queryKey, QueryFn = queryFn };
    }
}
