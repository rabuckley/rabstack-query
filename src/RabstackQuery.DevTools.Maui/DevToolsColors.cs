using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// MAUI <see cref="Color"/> wrappers around <see cref="DevToolsColorValues"/> hex constants,
/// plus theme-aware surface/chrome colors for the DevTools UI.
/// </summary>
internal static class DevToolsColors
{
    // ── Status colors (derived from shared hex constants) ───────────

    internal static readonly Color Fresh = Color.FromArgb(DevToolsColorValues.Fresh);
    internal static readonly Color Fetching = Color.FromArgb(DevToolsColorValues.Fetching);
    internal static readonly Color Paused = Color.FromArgb(DevToolsColorValues.Paused);
    internal static readonly Color Stale = Color.FromArgb(DevToolsColorValues.Stale);
    internal static readonly Color Inactive = Color.FromArgb(DevToolsColorValues.Inactive);
    internal static readonly Color Error = Color.FromArgb(DevToolsColorValues.Error);

    // ── Theme-aware surface colors (match example app DashboardPage) ─

    internal static readonly Color BorderLight = Color.FromArgb("#E0E0E0");
    internal static readonly Color BorderDark = Color.FromArgb("#444444");

    internal static readonly Color SecondaryTextLight = Color.FromArgb("#6B7280");
    internal static readonly Color SecondaryTextDark = Color.FromArgb("#9CA3AF");

    // ── Tab colors ──────────────────────────────────────────────────

    internal static readonly Color TabSelectedLight = Color.FromArgb("#1E1E1E");
    internal static readonly Color TabSelectedDark = Colors.White;
    internal static readonly Color TabUnselectedLight = Color.FromArgb("#9CA3AF");
    internal static readonly Color TabUnselectedDark = Color.FromArgb("#6B7280");
    internal static readonly Color TabIndicator = Fetching;

    // ── FAB colors ──────────────────────────────────────────────────

    internal static readonly Color FabBackgroundLight = Color.FromRgba(0x33, 0x33, 0x33, 0xCC);
    internal static readonly Color FabBackgroundDark = Color.FromRgba(0x55, 0x55, 0x55, 0xCC);

    // ── Legend items per tab ─────────────────────────────────────────

    internal static IReadOnlyList<(string Label, Color Color)> QueryLegendItems { get; } =
    [
        ("Fresh", Fresh), ("Fetching", Fetching), ("Paused", Paused),
        ("Stale", Stale), ("Inactive", Inactive), ("Error", Error),
    ];

    internal static IReadOnlyList<(string Label, Color Color)> MutationLegendItems { get; } =
    [
        ("Idle", Inactive), ("Pending", Fetching),
        ("Success", Fresh), ("Error", Error),
    ];

    // ── Font sizes ────────────────────────────────────────────────
    // Device.GetNamedSize / NamedSize are deprecated in .NET 10.
    // Use explicit sizes that approximate platform conventions.

    internal const double FontCaption = 12;
    internal const double FontSmall = 13;
    internal const double FontBody = 15;

    // ── Status mapping (MAUI Color from shared display status) ────

    internal static Color ForQueryStatus(QueryDisplayStatus status) => status switch
    {
        QueryDisplayStatus.Fresh => Fresh,
        QueryDisplayStatus.Fetching => Fetching,
        QueryDisplayStatus.Paused => Paused,
        QueryDisplayStatus.Stale => Stale,
        QueryDisplayStatus.Inactive => Inactive,
        QueryDisplayStatus.Error => Error,
        _ => Inactive,
    };

    internal static Color ForMutationStatus(MutationStatus status) => status switch
    {
        MutationStatus.Idle => Inactive,
        MutationStatus.Pending => Fetching,
        MutationStatus.Success => Fresh,
        MutationStatus.Error => Error,
        _ => Inactive,
    };

    /// <summary>
    /// Cross-platform monospace font family.
    /// </summary>
    internal static string MonospaceFont =>
        OperatingSystem.IsAndroid() ? "monospace" : "Menlo";

    /// <summary>
    /// Shorthand for SetAppThemeColor with
    /// light/dark color pairs.
    /// </summary>
    internal static void SetThemeColor(
        this VisualElement element, BindableProperty property, Color light, Color dark)
    {
        element.SetAppThemeColor(property, light, dark);
    }

    /// <summary>
    /// Applies the standard secondary text color (gray that adapts to light/dark).
    /// </summary>
    internal static void SetSecondaryTextColor(this VisualElement element)
    {
        element.SetAppThemeColor(Label.TextColorProperty, SecondaryTextLight, SecondaryTextDark);
    }
}
