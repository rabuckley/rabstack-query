namespace RabstackQuery;

/// <summary>
/// Dispatched after all error callbacks complete.
/// Transitions to <see cref="MutationStatus.Error"/> and increments failure count.
/// Mirrors TanStack's <c>dispatch({ type: 'error' })</c>.
/// </summary>
internal sealed class MutationErrorAction : MutationDispatchAction
{
    public required Exception Error { get; init; }
}
