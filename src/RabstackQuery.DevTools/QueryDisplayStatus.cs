namespace RabstackQuery.DevTools;

/// <summary>
/// Compound display status derived from <see cref="QueryStatus"/>,
/// <see cref="FetchStatus"/>, observer count, and staleness. Priority
/// order matches TanStack React Query DevTools.
/// </summary>
public enum QueryDisplayStatus
{
    Fresh,
    Fetching,
    Paused,
    Stale,
    Inactive,
    Error,
}
