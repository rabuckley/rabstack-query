namespace RabstackQuery.DevTools;

/// <summary>
/// Immutable snapshot of a single query's state for display in the devtools list.
/// </summary>
public sealed record QueryListItem
{
    public required string QueryHash { get; init; }
    public required string QueryKeyDisplay { get; init; }
    public required QueryDisplayStatus DisplayStatus { get; init; }
    public required QueryStatus Status { get; init; }
    public required FetchStatus FetchStatus { get; init; }
    public required int ObserverCount { get; init; }
    public required bool IsStale { get; init; }
    public required bool IsDisabled { get; init; }
    public required long DataUpdatedAt { get; init; }
    public required string DataDisplay { get; init; }
    public required bool IsInvalidated { get; init; }
    public required int FetchFailureCount { get; init; }
    public required string? ErrorDisplay { get; init; }
    public required bool IsDevToolsTriggered { get; init; }

    /// <summary>
    /// Status color hex string for the indicator bar, derived from <see cref="DisplayStatus"/>.
    /// </summary>
    public string StatusColorHex => DevToolsColorValues.ForQueryStatus(DisplayStatus);

    public string StatusLabel => DisplayStatus.ToString();
}
