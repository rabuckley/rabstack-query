namespace RabstackQuery;

/// <summary>
/// Default implementation of focus management.
/// Platform-specific code should call SetFocused() based on lifecycle events.
/// </summary>
public sealed class FocusManager : IFocusManager
{
    private static readonly Lazy<FocusManager> _instance = new(() => new FocusManager());

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static IFocusManager Instance => _instance.Value;

    private bool _isFocused = true;

    public FocusManager()
    {
    }

    /// <summary>
    /// Gets whether the application currently has focus.
    /// </summary>
    public bool IsFocused => _isFocused;

    /// <summary>
    /// Event raised when focus state changes.
    /// </summary>
    public event EventHandler? FocusChanged;

    /// <summary>
    /// Sets the focus state and raises the FocusChanged event if the state changed.
    /// </summary>
    /// <param name="focused">True if the application gained focus, false if it lost focus.</param>
    public void SetFocused(bool focused)
    {
        if (_isFocused != focused)
        {
            _isFocused = focused;
            FocusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
