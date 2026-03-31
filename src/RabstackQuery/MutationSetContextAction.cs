namespace RabstackQuery;

/// <summary>
/// Dispatched after the <c>OnMutate</c> callback returns, storing the
/// context value for use by <c>OnError</c>/<c>OnSettled</c> rollback.
/// Does not fire cache notifications.
/// </summary>
internal sealed class MutationSetContextAction<TOnMutateResult> : MutationDispatchAction
{
    public required TOnMutateResult? Context { get; init; }
}
