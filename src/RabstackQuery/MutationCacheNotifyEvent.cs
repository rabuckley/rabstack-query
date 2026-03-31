namespace RabstackQuery;

/// <summary>
/// Base class for events emitted by the <see cref="MutationCache"/> when mutations or
/// observers are added, removed, or updated.
/// </summary>
/// <remarks>
/// This is a closed hierarchy — all concrete subclasses are defined within RabstackQuery
/// and are sealed. Do not create custom subclasses.
/// </remarks>
public abstract class MutationCacheNotifyEvent
{
    private protected MutationCacheNotifyEvent() { }
}
