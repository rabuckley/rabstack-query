namespace RabstackQuery;

/// <summary>
/// Represents defaults for <see cref="QueryClientDefaultOptions.Mutations"/> and
/// <see cref="QueryClient.SetMutationDefaults"/>.
/// </summary>
/// <remarks>
/// A <see langword="null"/> property inherits. Layers apply in order, each overriding the
/// previous one: client-wide defaults, matching key-prefix registrations in registration
/// order, then per-mutation options.
/// </remarks>
public sealed record MutationDefaults
{
    /// <summary>
    /// Gets the number of retries after a failed attempt.
    /// <see langword="null"/> inherits; the framework default is 0.
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>
    /// Gets the function that computes the delay before a retry from the failure count
    /// and error. <see langword="null"/> inherits; the framework default is exponential backoff.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    /// Gets the duration an unused mutation stays cached before garbage collection.
    /// <see langword="null"/> inherits; the framework default is 5 minutes.
    /// </summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Gets whether the mutation waits for network connectivity.
    /// <see langword="null"/> inherits; the framework default is <see cref="RabstackQuery.NetworkMode.Online"/>.
    /// </summary>
    public NetworkMode? NetworkMode { get; init; }
}
