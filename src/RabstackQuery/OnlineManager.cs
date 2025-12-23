namespace RabstackQuery;

/// <summary>
/// Default implementation of online/offline management.
/// Platform-specific code should call SetOnline() based on network state changes.
/// </summary>
public sealed class OnlineManager : IOnlineManager
{
    private static readonly Lazy<OnlineManager> _instance = new(() => new OnlineManager());

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static IOnlineManager Instance => _instance.Value;

    private bool _isOnline = true;

    public OnlineManager()
    {
    }

    /// <summary>
    /// Gets whether the device is currently online.
    /// </summary>
    public bool IsOnline => _isOnline;

    /// <summary>
    /// Event raised when online state changes.
    /// </summary>
    public event EventHandler? OnlineChanged;

    /// <summary>
    /// Sets the online state and raises the OnlineChanged event if the state changed.
    /// </summary>
    /// <param name="online">True if the device is online, false if offline.</param>
    public void SetOnline(bool online)
    {
        if (_isOnline != online)
        {
            _isOnline = online;
            OnlineChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
