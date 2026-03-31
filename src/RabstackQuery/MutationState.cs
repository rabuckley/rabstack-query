namespace RabstackQuery;

/// <summary>
/// Immutable state of a mutation operation. Replaced atomically by
/// <see cref="Mutation{TData,TError,TVariables,TOnMutateResult}"/>'s
/// Dispatch/Reducer, matching the <see cref="QueryState{TData}"/> pattern.
/// </summary>
public sealed record MutationState<TData, TVariables, TOnMutateResult>
{
    public TData? Data { get; init; }
    public Exception? Error { get; init; }
    public int FailureCount { get; init; }
    public Exception? FailureReason { get; init; }
    public bool IsPaused { get; init; }
    public MutationStatus Status { get; init; }
    public TVariables? Variables { get; init; }
    public long SubmittedAt { get; init; }
    public TOnMutateResult? Context { get; init; }
}
