namespace RabstackQuery;

/// <summary>
/// Per-key-prefix defaults registered via <see cref="QueryClient.SetMutationDefaults"/>.
/// When a mutation is created, all registered defaults whose key is a prefix of the
/// mutation's key are merged (in registration order) to form the key-level defaults.
/// </summary>
public sealed class MutationDefaults
{
    public required QueryKey MutationKey { get; init; }
    public int? Retry { get; init; }
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }
    public TimeSpan? GcTime { get; init; }
    public NetworkMode? NetworkMode { get; init; }
}
