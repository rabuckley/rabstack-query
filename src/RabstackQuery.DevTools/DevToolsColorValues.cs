namespace RabstackQuery.DevTools;

/// <summary>
/// Hex color constants matching TanStack Query DevTools conventions.
/// Platform-specific UI projects map these to their native color types.
/// </summary>
public static class DevToolsColorValues
{
    // ── Status colors (match TanStack) ──────────────────────────────

    public const string Fresh = "#22C55E";
    public const string Fetching = "#3B82F6";
    public const string Paused = "#A855F7";
    public const string Stale = "#EAB308";
    public const string Inactive = "#6B7280";
    public const string Error = "#EF4444";

    // ── Legend items ────────────────────────────────────────────────

    public static IReadOnlyList<(string Label, string ColorHex)> QueryLegendItems { get; } =
    [
        ("Fresh", Fresh), ("Fetching", Fetching), ("Paused", Paused),
        ("Stale", Stale), ("Inactive", Inactive), ("Error", Error),
    ];

    public static IReadOnlyList<(string Label, string ColorHex)> MutationLegendItems { get; } =
    [
        ("Idle", Inactive), ("Pending", Fetching),
        ("Success", Fresh), ("Error", Error),
    ];

    // ── Status mapping ──────────────────────────────────────────────

    public static string ForQueryStatus(QueryDisplayStatus status) => status switch
    {
        QueryDisplayStatus.Fresh => Fresh,
        QueryDisplayStatus.Fetching => Fetching,
        QueryDisplayStatus.Paused => Paused,
        QueryDisplayStatus.Stale => Stale,
        QueryDisplayStatus.Inactive => Inactive,
        QueryDisplayStatus.Error => Error,
        _ => Inactive,
    };

    public static string ForMutationStatus(MutationStatus status) => status switch
    {
        MutationStatus.Idle => Inactive,
        MutationStatus.Pending => Fetching,
        MutationStatus.Success => Fresh,
        MutationStatus.Error => Error,
        _ => Inactive,
    };
}
