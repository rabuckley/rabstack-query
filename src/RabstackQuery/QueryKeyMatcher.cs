namespace RabstackQuery;

// Keys compare by per-element encoding rather than CLR equality: 1 and 1L share a hash, so
// filters must treat them as equal to reach every key sharing a cache entry.
internal static class QueryKeyMatcher
{
    public static bool PartialMatchKey(QueryKey key, QueryKey filter)
    {
        if (ReferenceEquals(key, filter)) return true;
        return PartialMatchKey(QueryKeyEncoder.EncodeElements(key), QueryKeyEncoder.EncodeElements(filter));
    }

    /// <summary>
    /// Checks whether <paramref name="key"/> is an exact match for <paramref name="filter"/>
    /// (same length and all elements equal).
    /// </summary>
    public static bool ExactMatchKey(QueryKey key, QueryKey filter) =>
        ExactMatchKey(QueryKeyEncoder.EncodeElements(key), QueryKeyEncoder.EncodeElements(filter));

    public static bool PartialMatchKey(string[] key, string[] filter) =>
        filter.Length <= key.Length && key.AsSpan(0, filter.Length).SequenceEqual(filter);

    public static bool ExactMatchKey(string[] key, string[] filter) =>
        key.AsSpan().SequenceEqual(filter);

    // Callers scanning a cache encode the filter key once rather than once per entry.
    public static string[]? EncodeFilterKey(QueryKey? filterKey) =>
        filterKey is null ? null : QueryKeyEncoder.EncodeElements(filterKey);

    /// <summary>
    /// Applies all filters from <paramref name="filters"/> to a <paramref name="query"/>
    /// using AND logic. Returns true only when every specified filter passes.
    /// </summary>
    public static bool MatchQuery(Query query, QueryFilters filters) =>
        MatchQuery(query, filters, EncodeFilterKey(filters.QueryKey));

    /// <param name="encodedFilterKey">
    /// The result of <see cref="EncodeFilterKey"/> for <paramref name="filters"/>' key.
    /// </param>
    public static bool MatchQuery(Query query, QueryFilters filters, string[]? encodedFilterKey)
    {
        // Key filter
        if (encodedFilterKey is not null && query.EncodedKeyElements is { } queryKey)
        {
            var matches = filters.Exact
                ? ExactMatchKey(queryKey, encodedFilterKey)
                : PartialMatchKey(queryKey, encodedFilterKey);

            if (!matches) return false;
        }

        // Type filter (active / inactive)
        if (filters.Type is not QueryTypeFilter.All)
        {
            var isActive = query.IsActive();
            if (filters.Type is QueryTypeFilter.Active && !isActive) return false;
            if (filters.Type is QueryTypeFilter.Inactive && isActive) return false;
        }

        // Stale filter
        if (filters.Stale is not null)
        {
            var isStale = query.IsStale();
            if (filters.Stale.Value != isStale) return false;
        }

        // FetchStatus filter
        if (filters.FetchStatus is not null)
        {
            if (query.CurrentFetchStatus != filters.FetchStatus.Value) return false;
        }

        // Arbitrary predicate
        if (filters.Predicate is not null)
        {
            if (!filters.Predicate(query)) return false;
        }

        return true;
    }
}
