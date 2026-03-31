namespace RabstackQuery;

/// <summary>
/// Callback invoked when the <see cref="QueryCache"/> emits a <see cref="QueryCacheNotifyEvent"/>.
/// </summary>
public delegate void QueryCacheListener(QueryCacheNotifyEvent queryCacheEvent);
