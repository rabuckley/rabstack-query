namespace RabstackQuery;

/// <summary>
/// Per-call options for individual mutation invocations.
/// These callbacks override the mutation's default options for a specific call.
/// </summary>
public class MutateOptions<TData, TError, TVariables, TOnMutateResult>
    where TError : Exception
{
    /// <summary>
    /// Called after the mutation succeeds for this specific invocation.
    /// Overrides the mutation's default onSuccess callback.
    /// </summary>
    public Func<TData, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSuccess { get; set; }

    /// <summary>
    /// Called after the mutation fails for this specific invocation.
    /// Overrides the mutation's default onError callback.
    /// </summary>
    public Func<TError, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnError { get; set; }

    /// <summary>
    /// Called after the mutation completes (success or error) for this specific invocation.
    /// Overrides the mutation's default onSettled callback.
    /// </summary>
    public Func<TData?, TError?, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSettled { get; set; }
}

/// <summary>
/// Convenience subclass that defaults TError to <see cref="Exception"/> and
/// TOnMutateResult to <c>object?</c>.
/// </summary>
public class MutateOptions<TData, TVariables>
    : MutateOptions<TData, Exception, TVariables, object?>;
