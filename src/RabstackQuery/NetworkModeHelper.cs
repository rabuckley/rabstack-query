namespace RabstackQuery;

/// <summary>
/// Utility methods for <see cref="NetworkMode"/>. Mirrors TanStack's
/// <c>canFetch(networkMode)</c> from <c>retryer.ts:52–56</c>.
/// </summary>
public static class NetworkModeHelper
{
    /// <summary>
    /// Whether execution can start given the current network mode and online state.
    /// <see cref="NetworkMode.Online"/> requires <see cref="IOnlineManager.IsOnline"/>
    /// to be true; <see cref="NetworkMode.Always"/> and <see cref="NetworkMode.OfflineFirst"/>
    /// always return true.
    /// </summary>
    public static bool CanFetch(NetworkMode networkMode, IOnlineManager onlineManager)
        => networkMode is not NetworkMode.Online || onlineManager.IsOnline;
}
