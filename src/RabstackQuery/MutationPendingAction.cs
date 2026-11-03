namespace RabstackQuery;

internal sealed class MutationPendingAction<TVariables> : MutationDispatchAction
{
    public required TVariables Variables { get; init; }
    public required long SubmittedAt { get; init; }
    public required bool IsPaused { get; init; }
}
