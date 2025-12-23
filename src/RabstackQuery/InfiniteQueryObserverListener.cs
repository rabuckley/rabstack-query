namespace RabstackQuery;

/// <summary>
/// Listener delegate for <see cref="InfiniteQueryObserver{TData,TPageParam}"/>.
/// </summary>
public delegate void InfiniteQueryObserverListener<TData, TPageParam>(
    IInfiniteQueryResult<TData, TPageParam> result);
