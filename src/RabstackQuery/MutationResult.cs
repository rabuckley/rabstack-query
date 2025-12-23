namespace RabstackQuery;

/// <summary>
/// Concrete implementation of IMutationResult.
/// </summary>
public sealed class MutationResult<TData, TError, TVariables, TOnMutateResult> : IMutationResult<TData, TError>
    where TError : Exception
{
    private readonly MutationState<TData, TVariables, TOnMutateResult> _state;

    public MutationResult(MutationState<TData, TVariables, TOnMutateResult> state)
    {
        _state = state;
    }

    public TData? Data => _state.Data;

    public Exception? Error => _state.Error;

    public bool IsIdle => _state.Status == MutationStatus.Idle;

    public bool IsPending => _state.Status == MutationStatus.Pending;

    public bool IsSuccess => _state.Status == MutationStatus.Success;

    public bool IsError => _state.Status == MutationStatus.Error;

    public bool IsPaused => _state.IsPaused;

    public MutationStatus Status => _state.Status;

    public int FailureCount => _state.FailureCount;
}
