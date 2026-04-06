using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery;

/// <summary>
/// Snapshot of a mutation's current state, produced by a <see cref="MutationObserver{TData,TError,TVariables,TOnMutateResult}"/>.
/// </summary>
/// <remarks>
/// This interface is implemented by the framework. Consumers should not implement it directly.
/// </remarks>
public interface IMutationResult<TData, TError>
    where TError : Exception
{
    /// <summary>The last successfully returned data from the mutation.</summary>
    TData? Data { get; }

    /// <summary>The error from the most recent failed mutation attempt.</summary>
    Exception? Error { get; }

    /// <summary><see langword="true"/> if the mutation has not yet been invoked.</summary>
    bool IsIdle { get; }

    /// <summary><see langword="true"/> if the mutation is currently executing.</summary>
    bool IsPending { get; }

    /// <summary><see langword="true"/> if the most recent mutation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Data))]
    bool IsSuccess { get; }

    /// <summary><see langword="true"/> if the most recent mutation failed.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    bool IsError { get; }

    /// <summary><see langword="true"/> if the mutation is paused (e.g., waiting for network connectivity).</summary>
    bool IsPaused { get; }

    /// <summary>
    /// The overall mutation status: <see cref="MutationStatus.Idle"/>,
    /// <see cref="MutationStatus.Pending"/>, <see cref="MutationStatus.Success"/>,
    /// or <see cref="MutationStatus.Error"/>.
    /// </summary>
    MutationStatus Status { get; }

    /// <summary>The number of times this mutation has failed consecutively.</summary>
    int FailureCount { get; }
}
