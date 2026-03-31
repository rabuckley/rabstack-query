namespace RabstackQuery;

/// <summary>
/// Cache-level configuration for a query, including garbage collection time,
/// retry policy, network mode, and initial data seeding.
/// </summary>
public sealed class QueryConfiguration<TData>
{
    public TimeSpan GcTime { get; set; }

    public string? QueryHash { get; set; }

    public QueryKey? QueryKey { get; set; }

    public IQueryKeyHasher? QueryKeyHasher { get; set; }

    /// <summary>
    /// Controls whether fetch execution is gated on network connectivity.
    /// Defaults to <see cref="NetworkMode.Online"/> — fetches pause when offline
    /// and resume when connectivity is restored. Mirrors TanStack's
    /// <c>queryOptions.networkMode</c>.
    /// </summary>
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Online;

    private TData? _initialData;

    /// <summary>
    /// Pre-populated cache data. Setting this property (even to <c>default</c>)
    /// marks the query as having initial data, which determines whether it starts
    /// in <see cref="QueryStatus.Succeeded"/> or <see cref="QueryStatus.Pending"/>.
    /// </summary>
    public TData? InitialData
    {
        get => _initialData;
        set
        {
            _initialData = value;
            HasInitialData = true;
        }
    }

    /// <summary>
    /// <see langword="true"/> when <see cref="InitialData"/> was explicitly assigned.
    /// Distinguishes "set to <c>default(TData)</c>" from "not set at all" — critical
    /// for value types where <c>default</c> is a valid data value (e.g. <c>0</c>
    /// for <see cref="int"/>). Without this flag, <c>default(int) = 0</c> would be
    /// indistinguishable from "no data", causing the query to start as
    /// <see cref="QueryStatus.Succeeded"/> instead of <see cref="QueryStatus.Pending"/>.
    /// </summary>
    internal bool HasInitialData { get; private set; }

    /// <summary>
    /// Factory function for deriving initial data, e.g. from another cached query.
    /// Takes precedence over <see cref="InitialData"/> when set.
    /// </summary>
    public Func<TData?>? InitialDataFactory { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds for when the initial data was last updated.
    /// Falls back to the current time if not set.
    /// </summary>
    public long? InitialDataUpdatedAt { get; set; }

    /// <summary>
    /// Factory function for deriving the initial data timestamp, e.g. from another
    /// cached query's <c>DataUpdatedAt</c>. Takes precedence over
    /// <see cref="InitialDataUpdatedAt"/> when set.
    /// </summary>
    public Func<long?>? InitialDataUpdatedAtFactory { get; set; }

    /// <summary>
    /// Number of retry attempts. 0 means no retries. Null means "not explicitly set" —
    /// the framework applies key/global defaults via the <c>??</c> chain in
    /// <see cref="QueryClient.DefaultQueryOptions{TData}"/>. The final fallback is 3.
    /// </summary>
    public int? Retry { get; set; }

    /// <summary>
    /// Custom retry delay function. Receives failure count and exception, returns delay.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; set; }

    /// <summary>
    /// Optional metadata for this query.
    /// </summary>
    public Meta? Meta { get; set; }
}
