namespace RabstackQuery;

/// <summary>
/// Configuration options for a mutation.
/// </summary>
public record MutationOptions<TData, TError, TVariables, TOnMutateResult>
    where TError : Exception
{
    /// <summary>
    /// Gets or initializes the mutation function.
    /// </summary>
    /// <remarks>
    /// Exceptions from this function, including <see cref="OperationCanceledException"/>,
    /// follow normal mutation error handling.
    /// </remarks>
    public Func<TVariables, MutationFunctionContext, Task<TData>>? MutationFn { get; init; }

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

/// <summary>
/// Creates <see cref="MutationOptions{TData, TVariables}"/> and
/// <see cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}"/> records with their type
/// arguments inferred from the supplied delegates.
/// </summary>
/// <remarks>
/// Add callbacks and settings with a <see langword="with"/> expression.
/// Use explicitly typed mutation lambda parameters for type inference, or explicit type arguments
/// when passing a method group.
/// </remarks>
/// <example>
/// <code>
/// var addTodo = MutationOptions.Create(
///     (string title, MutationFunctionContext _) => api.AddTodoAsync(title))
///     with
///     {
///         OnSuccess = (_, _, _, context) => context.Client.InvalidateQueriesAsync(["todos"]),
///     };
/// </code>
/// </example>
public static class MutationOptions
{
    /// <summary>
    /// Creates mutation options for <paramref name="mutationFn"/>, inferring
    /// <typeparamref name="TData"/> and <typeparamref name="TVariables"/> from it.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <param name="mutationFn">The mutation function.</param>
    /// <returns>Options whose <c>MutationFn</c> is <paramref name="mutationFn"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutationFn"/> is <see langword="null"/>.</exception>
    public static MutationOptions<TData, TVariables> Create<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, Task<TData>> mutationFn)
    {
        ArgumentNullException.ThrowIfNull(mutationFn);
        return new MutationOptions<TData, TVariables> { MutationFn = mutationFn };
    }

    /// <summary>
    /// Creates mutation options for an optimistic update, inferring <typeparamref name="TOnMutateResult"/>
    /// from the value <paramref name="onMutate"/> returns for rollback.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <typeparam name="TOnMutateResult">The rollback state returned by <paramref name="onMutate"/>.</typeparam>
    /// <param name="mutationFn">The mutation function.</param>
    /// <param name="onMutate">
    /// Runs before <paramref name="mutationFn"/>. Its result is passed to <c>OnSuccess</c>, <c>OnError</c>
    /// and <c>OnSettled</c>.
    /// </param>
    /// <returns>Options with <see cref="Exception"/> as the error type.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mutationFn"/> or <paramref name="onMutate"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// When <paramref name="mutationFn"/> has typed parameters, <paramref name="onMutate"/> can be an
    /// untyped lambda.
    /// </remarks>
    public static MutationOptions<TData, Exception, TVariables, TOnMutateResult>
        Create<TData, TVariables, TOnMutateResult>(
            Func<TVariables, MutationFunctionContext, Task<TData>> mutationFn,
            Func<TVariables, MutationFunctionContext, Task<TOnMutateResult>> onMutate)
    {
        ArgumentNullException.ThrowIfNull(mutationFn);
        ArgumentNullException.ThrowIfNull(onMutate);
        return new MutationOptions<TData, Exception, TVariables, TOnMutateResult>
        {
            MutationFn = mutationFn,
            OnMutate = onMutate,
        };
    }
}
