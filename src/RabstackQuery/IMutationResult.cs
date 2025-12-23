namespace RabstackQuery;

/// <summary>
/// Result of a mutation operation for observation.
/// </summary>
public interface IMutationResult<TData, TError>
    where TError : Exception
{
    TData? Data { get; }
    Exception? Error { get; }
    bool IsIdle { get; }
    bool IsPending { get; }
    bool IsSuccess { get; }
    bool IsError { get; }
    bool IsPaused { get; }
    MutationStatus Status { get; }
    int FailureCount { get; }
}
