namespace RabstackQuery;

/// <summary>
/// Controls whether a query observer refetches on mount, window focus, or network reconnect.
/// Maps to TanStack's tri-state: <c>true</c> → WhenStale, <c>false</c> → Never,
/// <c>"always"</c> → Always.
/// </summary>
/// <remarks>
/// TODO: TanStack also supports a function variant <c>(query) => bool | "always"</c>
/// for dynamic per-query decisions. Deferred as a rare advanced use case.
/// </remarks>
public enum RefetchOnBehavior
{
    /// <summary>Refetch if data is stale (default). Equivalent to TanStack <c>true</c>.</summary>
    WhenStale,

    /// <summary>Never refetch on this event. Equivalent to TanStack <c>false</c>.</summary>
    Never,

    /// <summary>Always refetch regardless of staleness. Equivalent to TanStack <c>"always"</c>.</summary>
    Always
}
