namespace RabstackQuery;

/// <summary>
/// Manages application focus state for triggering refetches when app regains focus.
/// </summary>
public interface IFocusManager
{
    /// <summary>
    /// Gets whether the application currently has focus.
    /// </summary>
    bool IsFocused { get; }

    /// <summary>
    /// Event raised when focus state changes.
    /// </summary>
    event EventHandler? FocusChanged;

    /// <summary>
    /// Manually sets the focus state. Called by platform-specific integrations.
    /// </summary>
    void SetFocused(bool focused);
}
