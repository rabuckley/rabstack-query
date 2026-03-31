namespace RabstackQuery;

/// <summary>
/// Dispatched when a mutation begins execution. Clears stale fields from any
/// prior execution and transitions to <see cref="MutationStatus.Pending"/>.
/// Mirrors TanStack's <c>dispatch({ type: 'pending' })</c> (mutation.ts:352-364).
/// </summary>
internal sealed class MutationPendingAction<TVariables> : MutationDispatchAction
{
    public required TVariables Variables { get; init; }
    public required long SubmittedAt { get; init; }
    public required bool IsPaused { get; init; }
}
