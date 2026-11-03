namespace RabstackQuery;

/// <summary>
/// Represents the options for <see cref="QueryClient.QueryAsync{TData}(QueryExecuteOptions{TData})"/>.
/// </summary>
/// <typeparam name="TData">The type of the cached data.</typeparam>
/// <remarks>
/// Unset defaultable options inherit from key-prefix and client-wide <see cref="QueryDefaults"/>.
/// </remarks>
public sealed record QueryExecuteOptions<TData>
{
    private readonly TData? _initialData;

    public required QueryKey QueryKey { get; init; }

    /// <summary>
    /// Gets or initializes the function that fetches the data when the cached data is missing or stale.
    /// </summary>
    /// <remarks>
    /// With <see cref="SkipToken.QueryFn{TData}"/>, a call that needs to fetch fails instead.
    /// </remarks>
    public required Func<QueryFunctionContext, Task<TData>> QueryFn { get; init; }

    /// <summary>
    /// Gets or initializes the duration after which cached data is refetched.
    /// </summary>
    /// <value>
    /// <see cref="Timeout.InfiniteTimeSpan"/> never refetches existing data, even after invalidation.
    /// <see langword="null"/> inherits; the framework default is <see cref="TimeSpan.Zero"/>.
    /// </value>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>
    /// Gets or initializes a function that computes the stale duration from the cached query.
    /// When set, it takes precedence over <see cref="StaleTime"/>.
    /// </summary>
    public Func<Query<TData>, TimeSpan>? StaleTimeFn { get; init; }

    /// <summary>
    /// Gets or initializes the duration an unused query stays cached before garbage collection.
    /// </summary>
    /// <value><see langword="null"/> inherits; the framework default is 5 minutes.</value>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Gets or initializes the number of retries after a failed fetch.
    /// </summary>
    /// <value><see langword="null"/> inherits; the framework default is 0.</value>
    public int? Retry { get; init; }

    /// <summary>
    /// Gets or initializes the function that computes the delay before a retry from the failure
    /// count and exception.
    /// </summary>
    /// <value><see langword="null"/> inherits from the key-prefix and client-wide defaults.</value>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

    /// <summary>
    /// Gets or initializes the hasher that computes the cache identity.
    /// </summary>
    /// <value><see langword="null"/> uses <see cref="DefaultQueryKeyHasher.Instance"/>.</value>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Gets or initializes the data used to seed a query with no data.
    /// </summary>
    /// <remarks>
    /// Assigning this property, even to <see langword="default"/>, seeds the query. Seeded data
    /// counts as cached, so it is refetched only if it is stale.
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

    /// <summary>
    /// Gets or initializes when <see cref="InitialData"/> was last updated, in Unix milliseconds.
    /// </summary>
    /// <value><see langword="null"/> uses the current time.</value>
    public long? InitialDataUpdatedAt { get; init; }

    internal bool HasInitialData { get; private init; }

    internal QueryDefaults ToQueryDefaults() => new()
    {
        StaleTime = StaleTime,
        GcTime = GcTime,
        Retry = Retry,
        RetryDelay = RetryDelay,
        NetworkMode = NetworkMode,
    };
}
