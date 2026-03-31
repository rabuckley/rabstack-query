namespace RabstackQuery;

/// <summary>
/// Per-call options for individual mutation invocations.
/// These callbacks override the mutation's default options for a specific call.
/// </summary>
/// <remarks>
/// This class is sealed. For the common case where TError is <see cref="Exception"/>
/// and TOnMutateResult is <c>object?</c>, use <see cref="MutationObserver.Create{TData,TVariables}"/>
/// which infers these defaults.
/// </remarks>
public sealed class MutateOptions<TData, TError, TVariables, TOnMutateResult>
    where TError : Exception
{
    /// <summary>
    /// Called after the mutation succeeds for this specific invocation.
    /// Overrides the mutation's default onSuccess callback.
    /// </summary>
    public Func<TData, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSuccess { get; init; }

    /// <summary>
    /// Called after the mutation fails for this specific invocation.
    /// Overrides the mutation's default onError callback.
    /// </summary>
    public Func<TError, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnError { get; init; }

    /// <summary>
    /// Called after the mutation completes (success or error) for this specific invocation.
    /// Overrides the mutation's default onSettled callback.
    /// </summary>
    public Func<TData?, TError?, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSettled { get; init; }
}

