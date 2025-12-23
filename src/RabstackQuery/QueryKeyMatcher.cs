using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace RabstackQuery;

/// <summary>
/// Utilities for matching query keys and applying <see cref="QueryFilters"/>.
/// Mirrors TanStack Query's <c>partialMatchKey</c> and <c>matchQuery</c>
/// from <c>utils.ts</c>.
/// </summary>
internal static class QueryKeyMatcher
{
    /// <summary>
    /// Checks whether <paramref name="key"/> matches <paramref name="filter"/>.
    /// When <paramref name="filter"/> has fewer elements than <paramref name="key"/>,
    /// it acts as a prefix match. Each element pair is compared by serializing
    /// to sorted JSON (same approach as <see cref="DefaultQueryKeyHasher"/>).
    /// </summary>
    public static bool PartialMatchKey(QueryKey key, QueryKey filter)
    {
        if (ReferenceEquals(key, filter)) return true;

        var keyList = key.ToList();
        var filterList = filter.ToList();

        // A filter longer than the key can never match.
        if (filterList.Count > keyList.Count) return false;

        // Compare element-by-element up to the filter length.
        for (var i = 0; i < filterList.Count; i++)
        {
            if (!ElementsEqual(keyList[i], filterList[i])) return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether <paramref name="key"/> is an exact match for <paramref name="filter"/>
    /// (same length and all elements equal).
    /// </summary>
    public static bool ExactMatchKey(QueryKey key, QueryKey filter)
    {
        var keyList = key.ToList();
        var filterList = filter.ToList();

        if (keyList.Count != filterList.Count) return false;

        for (var i = 0; i < filterList.Count; i++)
        {
            if (!ElementsEqual(keyList[i], filterList[i])) return false;
        }

        return true;
    }

    /// <summary>
    /// Applies all filters from <paramref name="filters"/> to a <paramref name="query"/>
    /// using AND logic. Returns true only when every specified filter passes.
    /// </summary>
    public static bool MatchQuery(Query query, QueryFilters filters)
    {
        // Key filter
        if (filters.QueryKey is not null && query.QueryKey is not null)
        {
            var matches = filters.Exact
                ? ExactMatchKey(query.QueryKey, filters.QueryKey)
                : PartialMatchKey(query.QueryKey, filters.QueryKey);

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

    /// <summary>
    /// Compares two query-key elements by serializing each to sorted JSON.
    /// </summary>
    private static bool ElementsEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        // Fast path for common primitives
        if (a is string sa && b is string sb) return sa == sb;
        if (a.GetType().IsPrimitive && b.GetType().IsPrimitive) return a.Equals(b);

        // Fall back to sorted-JSON comparison for complex objects
        var jsonA = SerializeSorted(a);
        var jsonB = SerializeSorted(b);
        return jsonA == jsonB;
    }

    // QueryKey elements are primitives (strings, numbers, booleans) and simple
    // anonymous objects — all natively handled by System.Text.Json without
    // requiring unreferenced types or runtime codegen.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "QueryKey elements are JSON-serializable primitives and simple objects.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "QueryKey elements are JSON-serializable primitives and simple objects.")]
    private static string SerializeSorted(object value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        var sorted = JsonNodeUtils.SortJsonNode(node);
        return sorted?.ToJsonString() ?? "null";
    }
}
