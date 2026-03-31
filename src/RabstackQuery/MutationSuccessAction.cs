namespace RabstackQuery;

/// <summary>
/// Dispatched after all success callbacks complete without error.
/// Transitions to <see cref="MutationStatus.Success"/> and clears failure tracking.
/// Mirrors TanStack's <c>dispatch({ type: 'success' })</c>.
/// </summary>
internal sealed class MutationSuccessAction<TData> : MutationDispatchAction
{
    public required TData Data { get; init; }
}
