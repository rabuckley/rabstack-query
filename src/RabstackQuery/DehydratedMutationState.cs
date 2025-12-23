namespace RabstackQuery;

/// <summary>
/// Non-generic mirror of <see cref="MutationState{TData, TVariables, TOnMutateResult}"/>
/// for serialization.
/// </summary>
public sealed class DehydratedMutationState
{
    public object? Data { get; init; }

    public Exception? Error { get; init; }

    public int FailureCount { get; init; }

    public Exception? FailureReason { get; init; }

    public bool IsPaused { get; init; }

    public MutationStatus Status { get; init; }

    public object? Variables { get; init; }

    public long SubmittedAt { get; init; }

    public object? Context { get; init; }
}
