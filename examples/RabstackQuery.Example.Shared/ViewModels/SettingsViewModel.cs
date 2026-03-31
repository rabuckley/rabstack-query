using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RabstackQuery.Example.Shared.Services;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// ViewModel for the Settings panel. Wraps <see cref="MockApiSettings"/> properties as
/// observable properties for two-way UI binding, and provides commands for controlling
/// the demo environment (reset data, simulate offline, toggle focus).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly MockApiSettings _settings;
    private readonly MockTaskBoardApi _mockApi;
    private readonly QueryClient _client;

    // ── Mock API settings (two-way bound to UI) ──────────────────────────

    [ObservableProperty]
    public partial double ErrorRate { get; set; }

    [ObservableProperty]
    public partial int MinDelayMs { get; set; }

    [ObservableProperty]
    public partial int MaxDelayMs { get; set; }

    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; } = true;

    // ── Read-only cache stats ────────────────────────────────────────────

    [ObservableProperty]
    public partial int QueryCount { get; set; }

    [ObservableProperty]
    public partial int MutationCount { get; set; }

    public SettingsViewModel(MockApiSettings settings, MockTaskBoardApi mockApi, QueryClient client)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mockApi);
        ArgumentNullException.ThrowIfNull(client);

        _settings = settings;
        _mockApi = mockApi;
        _client = client;

        // Initialize observable properties from current settings
        ErrorRate = settings.ErrorRate;
        MinDelayMs = settings.MinDelayMs;
        MaxDelayMs = settings.MaxDelayMs;
        IsOffline = settings.SimulateOffline;
    }

    // ── Property change handlers push values back to settings ────────────

    partial void OnErrorRateChanged(double value) => _settings.ErrorRate = value;

    partial void OnMinDelayMsChanged(int value) => _settings.MinDelayMs = value;

    partial void OnMaxDelayMsChanged(int value) => _settings.MaxDelayMs = value;

    partial void OnIsOfflineChanged(bool value)
    {
        _settings.SimulateOffline = value;
        OnlineManager.Instance.SetOnline(!value);
    }

    partial void OnIsFocusedChanged(bool value)
    {
        FocusManager.Instance.SetFocused(value);
    }

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all mock data to its initial state and invalidates all cached queries
    /// so the UI refetches fresh data.
    /// </summary>
    [RelayCommand]
    private async Task ResetAllDataAsync()
    {
        _mockApi.ResetData();
        await _client.InvalidateQueriesAsync(new InvalidateQueryFilters());
    }

    /// <summary>
    /// Refreshes the cache statistics displayed in the settings panel.
    /// </summary>
    [RelayCommand]
    private void RefreshStats()
    {
        QueryCount = _client.QueryCache.GetAll().Count();
        MutationCount = _client.MutationCache.GetAll().Count();
    }

    // No subscriptions to clean up, but implements IDisposable for consistency
    // with the page code-behind disposal pattern.
    public void Dispose() { }
}
