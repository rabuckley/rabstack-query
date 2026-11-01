namespace RabstackQuery;

/// <summary>
/// Configuration options for a mutation.
/// </summary>
public record MutationOptions<TData, TError, TVariables, TOnMutateResult>
    where TError : Exception
{
    /// <summary>
    /// The mutation function to execute.
    /// </summary>
    /// <remarks>
    /// The parameter order (<c>TVariables, MutationFunctionContext, CancellationToken</c>)
    /// mirrors TanStack's mutation function signature rather than the .NET convention of
    /// placing <see cref="CancellationToken"/> last. This keeps the most frequently used
    /// parameter (<c>TVariables</c>) first and groups the two framework-provided parameters
    /// together, matching the mental model from the TypeScript source.
    /// </remarks>
    public Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>>? MutationFn { get; init; }

    /// <summary>
    /// Optional mutation key for identifying this mutation.
    /// </summary>
    public QueryKey? MutationKey { get; init; }

    /// <summary>
    /// Optional metadata for this mutation.
    /// </summary>
    public Meta? Meta { get; init; }

    /// <summary>
    /// Optional scope for mutation isolation and coordination. Mutations sharing
    /// the same <see cref="MutationScope.Id"/> run sequentially within that scope.
    /// </summary>
    public MutationScope? Scope { get; init; }

    /// <summary>
    /// Defines how this mutation behaves in relation to network connectivity.
    /// <see langword="null"/> (default) inherits from <see cref="QueryClient.SetMutationDefaults"/>
    /// and <see cref="QueryClientDefaultOptions.Mutations"/>, falling back to
    /// <see cref="RabstackQuery.NetworkMode.Online"/>.
    /// </summary>
    public NetworkMode? NetworkMode { get; init; }

    /// <summary>
    /// Called before the mutation function is fired.
    /// Return value is stored as context and passed to all subsequent callbacks.
    /// Useful for optimistic updates.
    /// </summary>
    public Func<TVariables, MutationFunctionContext, Task<TOnMutateResult>>? OnMutate { get; init; }

    /// <summary>
    /// Called after the mutation succeeds.
    /// </summary>
    public Func<TData, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSuccess { get; init; }

    /// <summary>
    /// Called after the mutation fails.
    /// </summary>
    public Func<TError, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnError { get; init; }

    /// <summary>
    /// Called after the mutation completes (success or error).
    /// </summary>
    public Func<TData?, TError?, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSettled { get; init; }

    /// <summary>
    /// Number of retries after a failed attempt. 0 means no retries.
    /// <see langword="null"/> (default) inherits from <see cref="QueryClient.SetMutationDefaults"/>
    /// and <see cref="QueryClientDefaultOptions.Mutations"/>, falling back to 0.
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>
    /// Custom retry delay function. Receives failure count and exception.
    /// Returns delay as a <see cref="TimeSpan"/>. <see langword="null"/> (default) inherits
    /// from the key-prefix and client-wide defaults, falling back to exponential backoff.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    /// Duration before inactive mutations are garbage collected.
    /// <see cref="TimeSpan.Zero"/> removes the mutation as soon as it is unused.
    /// <see langword="null"/> (default) inherits from <see cref="QueryClient.SetMutationDefaults"/>
    /// and <see cref="QueryClientDefaultOptions.Mutations"/>, falling back to 5 minutes.
    /// </summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Gets or initializes a value that indicates whether defaults have already been applied.
    /// When <see langword="true"/>, <see cref="QueryClient.DefaultMutationOptions{TData,TError,TVariables,TOnMutateResult}"/>
    /// returns these options unchanged.
    /// </summary>
    public bool Defaulted { get; init; }

    internal MutationDefaults ToMutationDefaults() => new()
    {
        Retry = Retry,
        RetryDelay = RetryDelay,
        GcTime = GcTime,
        NetworkMode = NetworkMode,
    };
}

/// <summary>
/// Convenience subclass that defaults TError to <see cref="Exception"/> and
/// TOnMutateResult to <c>object?</c> for the common case where typed errors and
/// optimistic update context are not needed.
/// </summary>
public record MutationOptions<TData, TVariables>
    : MutationOptions<TData, Exception, TVariables, object?>;
