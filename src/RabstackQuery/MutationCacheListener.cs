namespace RabstackQuery;

/// <summary>
/// Callback invoked when the <see cref="MutationCache"/> emits a <see cref="MutationCacheNotifyEvent"/>.
/// </summary>
public delegate void MutationCacheListener(MutationCacheNotifyEvent cacheEvent);
