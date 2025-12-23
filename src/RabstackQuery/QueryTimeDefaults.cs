namespace RabstackQuery;

/// <summary>
/// Shared default durations for query and mutation lifecycle timers.
/// Centralises magic numbers that were previously scattered as <c>5 * 60 * 1000</c>.
/// </summary>
internal static class QueryTimeDefaults
{
    /// <summary>
    /// Default garbage-collection time: 5 minutes. Queries and mutations with no
    /// observers are removed from the cache after this duration.
    /// </summary>
    public static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
}
