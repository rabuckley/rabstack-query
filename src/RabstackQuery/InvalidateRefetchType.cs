namespace RabstackQuery;

/// <summary>
/// Controls which queries are automatically refetched after invalidation.
/// </summary>
public enum InvalidateRefetchType
{
    /// <summary>Refetch only queries with at least one observer (default).</summary>
    Active,

    /// <summary>Refetch only queries with zero observers.</summary>
    Inactive,

    /// <summary>Refetch all matching queries regardless of observer count.</summary>
    All,

    /// <summary>Don't refetch — only mark queries as invalidated.</summary>
    None
}
