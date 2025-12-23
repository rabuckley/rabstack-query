namespace RabstackQuery;

/// <summary>
/// Manages network connectivity state for triggering refetches when connection is restored.
/// </summary>
public interface IOnlineManager
{
    /// <summary>
    /// Gets whether the device is currently online.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Event raised when online state changes.
    /// </summary>
    event EventHandler? OnlineChanged;

    /// <summary>
    /// Manually sets the online state. Called by platform-specific integrations.
    /// </summary>
    void SetOnline(bool online);
}
