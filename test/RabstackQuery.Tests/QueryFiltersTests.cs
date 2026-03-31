namespace RabstackQuery;

public class QueryFiltersTests
{
    [Fact]
    public async Task InvalidateQueriesAsync_WithPartialKey_InvalidatesMatchingQueries()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var todo1 = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5) });
        var todo2 = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 2], GcTime = TimeSpan.FromMinutes(5) });
        var users = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5) });

        // Act — invalidate all "todos" queries via prefix match
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = ["todos"] });

        // Assert
        Assert.True(todo1.State!.IsInvalidated);
        Assert.True(todo2.State!.IsInvalidated);
        Assert.False(users.State!.IsInvalidated);
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Exact_OnlyInvalidatesExactMatch()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var todosAll = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });
        var todos1 = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5) });

        // Act
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = ["todos"], Exact = true });

        // Assert
        Assert.True(todosAll.State!.IsInvalidated);
        Assert.False(todos1.State!.IsInvalidated);
    }

    [Fact]
    public async Task InvalidateQueriesAsync_NullFilters_InvalidatesAll()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var todo = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });
        var user = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5) });

        await client.InvalidateQueriesAsync();

        Assert.True(todo.State!.IsInvalidated);
        Assert.True(user.State!.IsInvalidated);
    }

    [Fact]
    public void RemoveQueries_RemovesMatchingFromCache()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5) });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 2], GcTime = TimeSpan.FromMinutes(5) });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5) });

        // Act
        client.RemoveQueries(new QueryFilters { QueryKey = ["todos"] });

        // Assert
        var remaining = cache.GetAll().ToList();
        Assert.Single(remaining);
        Assert.Equal("users", remaining[0].QueryKey!.First()!.ToString());
    }

    [Fact]
    public void ResetQueries_ResetsMatchingToInitialState()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        // Build a query with initial data so it starts as Succeeded
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string>
            {
                QueryKey = ["todos"],
                GcTime = TimeSpan.FromMinutes(5),
                InitialData = "initial"
            });

        // Confirm it started with data
        Assert.Equal("initial", query.State!.Data);

        // Act — reset returns it to the default state (which still has InitialData)
        client.ResetQueries(new QueryFilters { QueryKey = ["todos"] });

        // After reset, state should be the default state from options
        Assert.Equal("initial", query.State!.Data);
        Assert.False(query.State!.IsInvalidated);
    }

    [Fact]
    public void FindAll_ReturnsQueriesMatchingFilters()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5) });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 2], GcTime = TimeSpan.FromMinutes(5) });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5) });

        var result = cache.FindAll(new QueryFilters { QueryKey = ["todos"] }).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Find_ReturnsFirstExactMatch()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5) });

        var result = cache.Find(new QueryFilters { QueryKey = ["todos"] });

        Assert.NotNull(result);
        // The exact match returns the query with key ["todos"], not ["todos", 1]
        var keyList = result.QueryKey!.ToList();
        Assert.Single(keyList);
    }

    [Fact]
    public void IsFetching_ReturnsZero_WhenNothingFetching()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5) });

        Assert.Equal(0, client.IsFetching());
    }

    [Fact]
    public void GetQueriesData_ReturnsDataForMatchingQueries()
    {
        var client = CreateQueryClient();

        // Seed some data
        client.SetQueryData(["todos", 1], "todo-1");
        client.SetQueryData(["todos", 2], "todo-2");
        client.SetQueryData(["users", 1], "user-1");

        var result = client.GetQueriesData<string>(new QueryFilters { QueryKey = ["todos"] }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Data == "todo-1");
        Assert.Contains(result, r => r.Data == "todo-2");
    }

    [Fact]
    public void SetQueriesData_UpdatesMatchingQueries()
    {
        var client = CreateQueryClient();

        client.SetQueryData(["todos", 1], "old-1");
        client.SetQueryData(["todos", 2], "old-2");
        client.SetQueryData(["users", 1], "user-1");

        // Act — update all todo queries
        client.SetQueriesData<string>(
            new QueryFilters { QueryKey = ["todos"] },
            old => old + "-updated");

        // Assert
        Assert.Equal("old-1-updated", client.GetQueryData<string>(["todos", 1]));
        Assert.Equal("old-2-updated", client.GetQueryData<string>(["todos", 2]));
        Assert.Equal("user-1", client.GetQueryData<string>(["users", 1]));
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
