namespace RabstackQuery;

/// <summary>
/// Context passed to <c>GetNextPageParam</c> and <c>GetPreviousPageParam</c> delegates.
/// Replaces an unwieldy 4-parameter <c>Func</c>. Mirrors TanStack's
/// <c>GetNextPageParamFunction</c> parameters from <c>infiniteQueryBehavior.ts</c>.
/// </summary>
public sealed class PageParamContext<TData, TPageParam>
{
    /// <summary>The data from the most recently fetched page.</summary>
    public required TData Page { get; init; }

    /// <summary>All pages fetched so far.</summary>
    public required IReadOnlyList<TData> AllPages { get; init; }

    /// <summary>The page param used to fetch the most recent page.</summary>
    public required TPageParam PageParam { get; init; }

    /// <summary>All page params used so far.</summary>
    public required IReadOnlyList<TPageParam> AllPageParams { get; init; }
}
