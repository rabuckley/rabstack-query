namespace RabstackQuery;

/// <summary>
/// Per-key-prefix defaults registered via <see cref="QueryClient.SetQueryDefaults"/>.
/// When a query is created, all registered defaults whose key is a prefix of the
/// query's key are merged (in registration order) to form the key-level defaults.
/// </summary>
public sealed class QueryDefaults
{
    public required QueryKey QueryKey { get; init; }
    public TimeSpan? StaleTime { get; init; }
    public TimeSpan? GcTime { get; init; }
    public int? Retry { get; init; }
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }
    public RefetchOnBehavior? RefetchOnWindowFocus { get; init; }
    public RefetchOnBehavior? RefetchOnReconnect { get; init; }
    public RefetchOnBehavior? RefetchOnMount { get; init; }
}
