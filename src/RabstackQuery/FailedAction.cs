namespace RabstackQuery;

/// <summary>
/// Dispatched when an individual retry attempt fails but retries remain.
/// Increments the failure count without transitioning to error status.
/// </summary>
internal sealed class FailedAction : DispatchAction
{
    public required int FailureCount { get; init; }
    public required Exception Error { get; init; }
}
