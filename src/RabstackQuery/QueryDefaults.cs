namespace RabstackQuery;

/// <summary>
/// Represents defaults for <see cref="QueryClientDefaultOptions.Queries"/> and
/// <see cref="QueryClient.SetQueryDefaults"/>.
/// </summary>
/// <remarks>
/// A <see langword="null"/> property inherits. Layers apply in order, each overriding the
/// previous one: client-wide defaults, matching key-prefix registrations in registration
/// order, then per-query options.
/// </remarks>
public sealed record QueryDefaults
{
    /// <summary>
    /// Gets the duration after which data becomes stale.
    /// <see langword="null"/> inherits; the framework default is <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>
    /// Gets the duration an unused query stays cached before garbage collection.
    /// <see langword="null"/> inherits; the framework default is 5 minutes.
    /// </summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Gets the number of retries after a failed fetch.
    /// <see langword="null"/> inherits; the framework default is 3 for observers and 0
    /// for imperative fetches such as <see cref="QueryClient.QueryAsync{TData}(QueryExecuteOptions{TData})"/>.
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>
    /// Gets the function that computes the delay before a retry from the failure count
    /// and error. <see langword="null"/> inherits; the framework default is exponential backoff.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    /// Gets whether an observer refetches when the window regains focus.
    /// <see langword="null"/> inherits; the framework default is <see cref="RefetchOnBehavior.WhenStale"/>.
    /// </summary>
    public RefetchOnBehavior? RefetchOnWindowFocus { get; init; }

    /// <summary>
    /// Gets whether an observer refetches when network connectivity returns.
    /// <see langword="null"/> inherits; when no layer sets it, the default is
    /// <see cref="RefetchOnBehavior.Never"/> if the resolved <see cref="NetworkMode"/> is
    /// <see cref="RabstackQuery.NetworkMode.Always"/>, and <see cref="RefetchOnBehavior.WhenStale"/> otherwise.
    /// </summary>
    public RefetchOnBehavior? RefetchOnReconnect { get; init; }

    /// <summary>
    /// Gets whether an observer refetches existing data when it subscribes.
    /// <see langword="null"/> inherits; the framework default is <see cref="RefetchOnBehavior.WhenStale"/>.
    /// </summary>
    public RefetchOnBehavior? RefetchOnMount { get; init; }

    /// <summary>
    /// Gets whether fetches wait for network connectivity.
    /// <see langword="null"/> inherits; the framework default is <see cref="RabstackQuery.NetworkMode.Online"/>.
    /// </summary>
    public NetworkMode? NetworkMode { get; init; }
}
