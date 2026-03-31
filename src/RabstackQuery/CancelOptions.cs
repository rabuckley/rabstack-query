namespace RabstackQuery;

/// <summary>
/// Options for <see cref="Query.Cancel"/>.
/// </summary>
public sealed record CancelOptions
{
    /// <summary>
    /// When true, revert the query to its pre-fetch state on cancellation.
    /// </summary>
    public bool Revert { get; init; }

    /// <summary>
    /// When true, suppress observer notifications for the cancellation.
    /// </summary>
    public bool Silent { get; init; }
}
