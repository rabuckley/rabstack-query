namespace RabstackQuery;

/// <summary>
/// Global default options applied to all queries created by a <see cref="QueryClient"/>.
/// These form the lowest-precedence layer in the three-level merge:
/// global defaults → per-key-prefix defaults → per-query options.
/// </summary>
public sealed class QueryClientDefaultOptions
{
    /// <summary>
    /// Duration after which data becomes stale. Null means use the
    /// framework default (<see cref="TimeSpan.Zero"/> = always stale).
    /// </summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>
    /// Duration before inactive queries are garbage collected.
    /// Null means use the framework default (5 minutes).
    /// </summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Number of retry attempts. Null means use the framework default (3).
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>
    /// Custom retry delay function.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    /// Default options for <see cref="QueryClient.Dehydrate"/>.
    /// </summary>
    public DehydrateOptions? Dehydrate { get; init; }

    /// <summary>
    /// Default options for <see cref="QueryClient.Hydrate"/>.
    /// </summary>
    public HydrateOptions? Hydrate { get; init; }
}
