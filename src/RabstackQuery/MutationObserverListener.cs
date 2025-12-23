namespace RabstackQuery;

/// <summary>
/// Listener delegate for mutation result changes.
/// </summary>
public delegate void MutationObserverListener<TData, TError>(IMutationResult<TData, TError> result)
    where TError : Exception;
