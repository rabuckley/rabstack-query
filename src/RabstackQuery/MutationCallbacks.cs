namespace RabstackQuery;

/// <summary>
/// Simplified mutation lifecycle callbacks for the common case where
/// TError is <see cref="Exception"/> and no typed OnMutate context is needed.
/// Users who need optimistic updates (OnMutate) should use the full
/// <see cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}"/>.
/// </summary>
public sealed class MutationCallbacks<TData, TVariables>
{
    /// <summary>Called after the mutation succeeds.</summary>
    public Func<TData, TVariables, MutationFunctionContext, Task>? OnSuccess { get; init; }

    /// <summary>Called after the mutation fails.</summary>
    public Func<Exception, TVariables, MutationFunctionContext, Task>? OnError { get; init; }

    /// <summary>Called after the mutation completes (success or error).</summary>
    public Func<TData?, Exception?, TVariables, MutationFunctionContext, Task>? OnSettled { get; init; }

    /// <summary>
    /// Converts to the full <see cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}"/>
    /// by mapping each 3-param callback to the 4-param signature (passing <c>null</c> for
    /// the <c>TOnMutateResult?</c> parameter that doesn't exist in the simplified form).
    /// </summary>
    internal MutationOptions<TData, Exception, TVariables, object?> ToMutationOptions()
    {
        return new MutationOptions<TData, Exception, TVariables, object?>
        {
            OnSuccess = OnSuccess is { } onSuccess
                ? (data, vars, _, ctx) => onSuccess(data, vars, ctx)
                : null,
            OnError = OnError is { } onError
                ? (err, vars, _, ctx) => onError(err, vars, ctx)
                : null,
            OnSettled = OnSettled is { } onSettled
                ? (data, err, vars, _, ctx) => onSettled(data, err, vars, ctx)
                : null,
        };
    }
}
