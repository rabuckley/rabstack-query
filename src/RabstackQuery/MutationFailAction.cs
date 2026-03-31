namespace RabstackQuery;

/// <summary>
/// Dispatched when a retry attempt fails. Updates failure tracking fields
/// without changing the mutation's overall status.
/// Mirrors TanStack's retryer <c>onFail</c> callback.
/// </summary>
internal sealed class MutationFailAction : MutationDispatchAction
{
    public required int FailureCount { get; init; }
    public required Exception Error { get; init; }
}
