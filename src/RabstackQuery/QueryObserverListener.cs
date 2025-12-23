namespace RabstackQuery;

public delegate void QueryObserverListener<TData>(IQueryResult<TData> result);
