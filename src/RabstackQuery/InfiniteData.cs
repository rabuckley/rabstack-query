namespace RabstackQuery;

/// <summary>
/// Container for paginated query data. Holds a list of pages and their
/// corresponding page params. Mirrors TanStack's <c>InfiniteData</c> type
/// from <c>types.ts</c>.
/// </summary>
public sealed class InfiniteData<TData, TPageParam>
{
    /// <summary>The fetched pages in order.</summary>
    public required IReadOnlyList<TData> Pages { get; init; }

    /// <summary>
    /// The page param used to fetch each corresponding page.
    /// <c>PageParams[i]</c> was used to fetch <c>Pages[i]</c>.
    /// </summary>
    public required IReadOnlyList<TPageParam> PageParams { get; init; }
}
