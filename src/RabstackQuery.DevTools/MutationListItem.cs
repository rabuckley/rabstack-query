namespace RabstackQuery.DevTools;

/// <summary>
/// Immutable snapshot of a single mutation's state for display in the devtools list.
/// Includes dehydrated data from <see cref="Mutation.Dehydrate()"/>.
/// </summary>
public sealed record MutationListItem
{
    public required int MutationId { get; init; }
    public required string? MutationKeyDisplay { get; init; }
    public required MutationStatus Status { get; init; }
    public required bool IsPaused { get; init; }
    public required bool HasObservers { get; init; }

    // Rich fields populated via Mutation.Dehydrate()
    public string? DataDisplay { get; init; }
    public string? VariablesDisplay { get; init; }
    public long SubmittedAt { get; init; }
    public string? ErrorDisplay { get; init; }
    public int FailureCount { get; init; }

    public string StatusColorHex => DevToolsColorValues.ForMutationStatus(Status);

    public string StatusLabel => IsPaused ? "Paused" : Status.ToString();
}
