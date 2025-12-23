namespace RabstackQuery;

/// <summary>
/// Configuration for cache-level mutation callbacks. These callbacks fire for every
/// mutation that executes through a <see cref="MutationCache"/>. Cache-level callbacks
/// run <em>before</em> the corresponding mutation-level callbacks, allowing global
/// error handling, logging, or side effects (e.g., toast notifications on any error).
///
/// All callback parameters are type-erased (<c>object?</c> / <c>Exception</c>) because
/// a single cache stores mutations with heterogeneous type parameters.
///
/// Mirrors TanStack's <c>MutationCacheConfig</c> interface.
/// </summary>
public sealed class MutationCacheConfig
{
    /// <summary>
    /// Shared empty instance. Avoids allocating a new config when no callbacks are needed.
    /// </summary>
    public static readonly MutationCacheConfig Empty = new();

    /// <summary>
    /// Called before a mutation executes. Runs before the mutation-level
    /// <c>OnMutate</c> callback.
    /// </summary>
    public MutationCacheOnMutateCallback? OnMutate { get; init; }

    /// <summary>
    /// Called when a mutation succeeds. Runs before the mutation-level
    /// <c>OnSuccess</c> callback.
    /// </summary>
    public MutationCacheOnSuccessCallback? OnSuccess { get; init; }

    /// <summary>
    /// Called when a mutation errors. Runs before the mutation-level
    /// <c>OnError</c> callback.
    /// </summary>
    public MutationCacheOnErrorCallback? OnError { get; init; }

    /// <summary>
    /// Called after a mutation completes (success or error). Runs before the
    /// mutation-level <c>OnSettled</c> callback.
    /// </summary>
    public MutationCacheOnSettledCallback? OnSettled { get; init; }
}
