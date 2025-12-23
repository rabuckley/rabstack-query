using Microsoft.Maui.Storage;

using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Persists devtools UI preferences via the platform's native preferences
/// store (NSUserDefaults on iOS/Mac, SharedPreferences on Android).
/// </summary>
internal static class DevToolsState
{
    private const string Prefix = "RabstackQuery.DevTools.";

    internal static int SelectedTab
    {
        get => Preferences.Get($"{Prefix}SelectedTab", 0);
        set => Preferences.Set($"{Prefix}SelectedTab", value);
    }

    internal static int SortOptionIndex
    {
        get => Preferences.Get($"{Prefix}SortOption", 0);
        set => Preferences.Set($"{Prefix}SortOption", value);
    }

    internal static SortOption CurrentSortOption
    {
        get => (SortOption)SortOptionIndex;
        set => SortOptionIndex = (int)value;
    }

    internal static bool HideDisabled
    {
        get => Preferences.Get($"{Prefix}HideDisabled", false);
        set => Preferences.Set($"{Prefix}HideDisabled", value);
    }
}
