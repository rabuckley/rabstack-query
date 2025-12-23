namespace RabstackQuery;

/// <summary>
/// State of a mutation operation.
/// </summary>
public sealed class MutationState<TData, TVariables, TOnMutateResult>
{
    public TData? Data { get; set; }
    public Exception? Error { get; set; }
    public int FailureCount { get; set; }
    public Exception? FailureReason { get; set; }
    public bool IsPaused { get; set; }
    public MutationStatus Status { get; set; }
    public TVariables? Variables { get; set; }
    public long SubmittedAt { get; set; }
    public TOnMutateResult? Context { get; set; }
}
