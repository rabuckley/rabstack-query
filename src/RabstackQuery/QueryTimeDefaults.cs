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

    /// <summary>
    /// Canonical staleness calculation shared by <see cref="QueryClient"/>,
    /// <see cref="QueryObserver{TData,TQueryData}"/>, and <see cref="QueryResult{TData}"/>.
    /// Each call site adds its own pre-checks (Enabled, null-data) then delegates here.
    /// </summary>
    public static bool IsStale(long dataUpdatedAt, TimeSpan staleTime, bool isInvalidated, long nowMs)
    {
        if (staleTime == Timeout.InfiniteTimeSpan) return false;
        if (staleTime == TimeSpan.Zero) return true;
        if (isInvalidated) return true;
        if (dataUpdatedAt == 0) return true;
        return (nowMs - dataUpdatedAt) >= staleTime.TotalMilliseconds;
    }
}
