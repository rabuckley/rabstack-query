namespace RabstackQuery;

/// <summary>
/// Defines how queries and mutations behave in relation to network connectivity.
/// </summary>
public enum NetworkMode
{
    /// <summary>
    /// Queries/mutations only run when online. Paused when offline.
    /// </summary>
    Online,

    /// <summary>
    /// Queries/mutations always run regardless of network status.
    /// </summary>
    Always,

    /// <summary>
    /// Run once; if network failure, pause until online.
    /// </summary>
    OfflineFirst
}
