namespace RabstackQuery;

/// <summary>
/// Base class for events emitted by the <see cref="QueryCache"/> when queries or
/// observers are added, removed, or updated.
/// </summary>
/// <remarks>
/// This is a closed hierarchy — all concrete subclasses are defined within RabstackQuery
/// and are sealed. Do not create custom subclasses.
/// </remarks>
public abstract class QueryCacheNotifyEvent
{
    private protected QueryCacheNotifyEvent() { }
}
