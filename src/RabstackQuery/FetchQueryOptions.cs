namespace RabstackQuery;

/// <summary>
/// Options for imperative fetch methods: <see cref="QueryClient.FetchQueryAsync{TData}"/>,
/// <see cref="QueryClient.PrefetchQueryAsync{TData}"/>, and
/// <see cref="QueryClient.EnsureQueryDataAsync{TData}"/>.
/// </summary>
[Obsolete("Use QueryExecuteOptions<TData> with QueryAsync instead. This type will be removed in the next major version.")]
public sealed class FetchQueryOptions<TData>
{
    private readonly TData? _initialData;

    /// <summary>Query key to fetch.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <summary>Query function — called when the cache is empty or stale.</summary>
    public required Func<QueryFunctionContext, Task<TData>> QueryFn { get; init; }

    /// <summary>
    /// Data older than this duration is considered stale and will be refetched.
    /// <see langword="null"/> inherits from <see cref="QueryClient.SetQueryDefaults"/> and
    /// <see cref="QueryClientDefaultOptions.Queries"/>, falling back to
    /// <see cref="TimeSpan.Zero"/> (always stale).
    /// </summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>
    /// Gets or initializes a function that computes the stale duration from the cached query.
    /// When set, it takes precedence over <see cref="StaleTime"/>.
    /// </summary>
    public Func<Query<TData>, TimeSpan>? StaleTimeFn { get; init; }

    /// <summary>
    /// Garbage collection duration. <see langword="null"/> inherits from the key-prefix and
    /// client-wide defaults, falling back to 5 minutes. Applies only when this call creates the query.
    /// </summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Gets or initializes the number of retries. <see langword="null"/> inherits from key-prefix
    /// and client-wide defaults, falling back to 0. Applies only when this call creates the query.
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>
    /// Custom retry delay function. Receives failure count and exception.
    /// <see langword="null"/> inherits from the key-prefix and client-wide defaults.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Gets or initializes the data that seeds a query with no data.
    /// </summary>
    /// <remarks>
    /// Assigning this property, even to <see langword="default"/>, seeds the query. Seeded data
    /// counts as cached.
    /// </remarks>
    public TData? InitialData
    {
        get => _initialData;
        init
        {
            _initialData = value;
            HasInitialData = true;
        }
    }

    internal bool HasInitialData { get; private init; }

    /// <summary>
    /// Gets or initializes when <see cref="InitialData"/> was last updated, in Unix milliseconds.
    /// </summary>
    /// <value><see langword="null"/> uses the current time.</value>
    public long? InitialDataUpdatedAt { get; init; }

    /// <summary>
    /// When true, <see cref="QueryClient.EnsureQueryDataAsync{TData}"/> triggers
    /// a background refetch if the cached (or initial) data is stale, while still
    /// returning the existing data immediately. Mirrors TanStack's
    /// <c>revalidateIfStale</c> option on <c>ensureQueryData</c>.
    /// </summary>
    public bool RevalidateIfStale { get; init; }

    internal QueryDefaults ToQueryDefaults() => new()
    {
        StaleTime = StaleTime,
        GcTime = GcTime,
        Retry = Retry,
        RetryDelay = RetryDelay,
        NetworkMode = NetworkMode,
    };

    internal QueryExecuteOptions<TData> ToQueryExecuteOptions()
    {
        var options = new QueryExecuteOptions<TData>
        {
            QueryKey = QueryKey,
            QueryFn = QueryFn,
            StaleTime = StaleTime,
            StaleTimeFn = StaleTimeFn,
            GcTime = GcTime,
            Retry = Retry,
            RetryDelay = RetryDelay,
            NetworkMode = NetworkMode,
            Meta = Meta,
            QueryKeyHasher = QueryKeyHasher,
            InitialDataUpdatedAt = InitialDataUpdatedAt,
        };

        return HasInitialData ? options with { InitialData = InitialData } : options;
    }
}

/// <summary>
/// Static factory for creating <see cref="FetchQueryOptions{TData}"/> with type inference
/// from the query function delegate.
/// </summary>
[Obsolete("Use QueryOptions.Create with QueryAsync instead. This type will be removed in the next major version.")]
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
