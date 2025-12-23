namespace RabstackQuery;

/// <summary>
/// Delegate for receiving result updates from <see cref="QueriesObserver{TData,TCombinedResult}"/>.
/// </summary>
public delegate void QueriesObserverListener<TData>(IReadOnlyList<IQueryResult<TData>> results);
