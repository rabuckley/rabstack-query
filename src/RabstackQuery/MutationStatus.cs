namespace RabstackQuery;

/// <summary>
/// Status of a mutation operation.
/// </summary>
public enum MutationStatus
{
    /// <summary>
    /// Mutation has not been executed yet.
    /// </summary>
    Idle,

    /// <summary>
    /// Mutation is currently executing.
    /// </summary>
    Pending,

    /// <summary>
    /// Mutation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Mutation failed with an error.
    /// </summary>
    Error
}
