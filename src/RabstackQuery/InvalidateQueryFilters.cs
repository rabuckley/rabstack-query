namespace RabstackQuery;

/// <summary>
/// Extended filters for <see cref="QueryClient.InvalidateQueries"/> that add
/// control over which invalidated queries are automatically refetched.
/// Mirrors TanStack's <c>InvalidateQueryFilters</c>.
/// </summary>
public sealed class InvalidateQueryFilters : QueryFilters
{
    /// <summary>
    /// Controls which invalidated queries are refetched. Defaults to
    /// <see cref="InvalidateRefetchType.Active"/> (only queries with observers),
    /// matching TanStack's behavior.
    /// </summary>
    public InvalidateRefetchType RefetchType { get; init; } = InvalidateRefetchType.Active;
}
