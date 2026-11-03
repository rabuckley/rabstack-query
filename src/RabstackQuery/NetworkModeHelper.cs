namespace RabstackQuery;

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
