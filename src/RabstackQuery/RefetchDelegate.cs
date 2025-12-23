namespace RabstackQuery;

/// <summary>
/// Delegate matching <see cref="QueryObserver{TData,TQueryData}.RefetchAsync"/>,
/// captured when the result is created so that <see cref="QueryResult{TData}.RefetchAsync"/>
/// can forward to the observer without holding a direct reference.
/// </summary>
public delegate Task<IQueryResult<TData>> RefetchDelegate<TData>(
    RefetchOptions? options, CancellationToken cancellationToken);
