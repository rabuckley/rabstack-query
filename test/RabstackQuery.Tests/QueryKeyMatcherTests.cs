namespace RabstackQuery.Tests;

public class QueryKeyMatcherTests
{
    // ── PartialMatchKey ────────────────────────────────────────────

    [Fact]
    public void PartialMatchKey_PrefixMatch_ReturnsTrue()
    {
        QueryKey key = ["todos", 1, "comments"];
        QueryKey filter = ["todos"];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_ExactSameLength_ReturnsTrue()
    {
        QueryKey key = ["todos", 1];
        QueryKey filter = ["todos", 1];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_FilterLongerThanKey_ReturnsFalse()
    {
        QueryKey key = ["todos"];
        QueryKey filter = ["todos", 1, "comments"];

        Assert.False(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_DifferentFirstElement_ReturnsFalse()
    {
        QueryKey key = ["users", 1];
        QueryKey filter = ["todos"];

        Assert.False(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_ObjectElements_SamePropertyOrder_ReturnsTrue()
    {
        QueryKey key = ["todos", new { status = "done", page = 1 }];
        QueryKey filter = ["todos", new { status = "done", page = 1 }];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_ObjectElements_DifferentPropertyOrder_ReturnsTrue()
    {
        // Property order shouldn't matter — sorted JSON comparison
        QueryKey key = ["todos", new { status = "done", page = 1 }];
        QueryKey filter = ["todos", new { page = 1, status = "done" }];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_ObjectElements_DifferentValues_ReturnsFalse()
    {
        QueryKey key = ["todos", new { status = "done" }];
        QueryKey filter = ["todos", new { status = "active" }];

        Assert.False(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    [Fact]
    public void PartialMatchKey_SameReference_ReturnsTrue()
    {
        QueryKey key = ["todos"];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, key));
    }

    [Fact]
    public void PartialMatchKey_EmptyFilter_MatchesEverything()
    {
        QueryKey key = ["todos", 1, "comments"];
        QueryKey filter = [];

        Assert.True(QueryKeyMatcher.PartialMatchKey(key, filter));
    }

    // ── ExactMatchKey ──────────────────────────────────────────────

    [Fact]
    public void ExactMatchKey_SameElements_ReturnsTrue()
    {
        QueryKey key = ["todos", 1];
        QueryKey filter = ["todos", 1];

        Assert.True(QueryKeyMatcher.ExactMatchKey(key, filter));
    }

    [Fact]
    public void ExactMatchKey_PrefixOnly_ReturnsFalse()
    {
        QueryKey key = ["todos", 1];
        QueryKey filter = ["todos"];

        Assert.False(QueryKeyMatcher.ExactMatchKey(key, filter));
    }

    // ── MatchQuery (integrated with Query type filter, stale, etc.) ─

    [Fact]
    public void MatchQuery_NoFilters_MatchesAny()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var query = cache.Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });

        var filters = new QueryFilters();

        // Act & Assert
        Assert.True(QueryKeyMatcher.MatchQuery(query, filters));
    }

    [Fact]
    public void MatchQuery_TypeFilterActive_ExcludesNoObserverQuery()
    {
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var query = cache.Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });

        var filters = new QueryFilters { Type = QueryTypeFilter.Active };

        // No observers added, so query is inactive
        Assert.False(QueryKeyMatcher.MatchQuery(query, filters));
    }

    [Fact]
    public void MatchQuery_TypeFilterInactive_IncludesNoObserverQuery()
    {
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var query = cache.Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });

        var filters = new QueryFilters { Type = QueryTypeFilter.Inactive };

        Assert.True(QueryKeyMatcher.MatchQuery(query, filters));
    }

    [Fact]
    public void MatchQuery_PredicateFilter()
    {
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        cache.Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });
        var query2 = cache.Build<int, int>(client,
            new QueryConfiguration<int> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5) });

        // Predicate that only matches queries whose key starts with "users"
        var filters = new QueryFilters
        {
            Predicate = q => q.QueryKey is not null &&
                             q.QueryKey.First()?.ToString() == "users"
        };

        Assert.True(QueryKeyMatcher.MatchQuery(query2, filters));
    }

    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }
}
