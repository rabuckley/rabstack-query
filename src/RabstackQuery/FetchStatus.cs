namespace RabstackQuery;

/// <summary>
/// Network-level fetch status of a query, independent of <see cref="QueryStatus"/>.
/// </summary>
public enum FetchStatus
{
    /// <summary>A fetch request is currently in flight.</summary>
    Fetching,

    /// <summary>The query wanted to fetch but is paused due to network unavailability.</summary>
    Paused,

    /// <summary>No fetch is in progress.</summary>
    Idle,
}
