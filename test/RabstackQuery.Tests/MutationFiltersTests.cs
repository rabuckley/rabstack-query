namespace RabstackQuery.Tests;

public class MutationFiltersTests
{
    [Fact]
    public void FindAll_ByKey_ReturnsMatchingMutations()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "create"]
            });
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "update"]
            });
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["users", "create"]
            });

        // Act — prefix match
        var result = cache.FindAll(new MutationFilters { MutationKey = ["todos"] }).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FindAll_ByStatus_ReturnsMatchingMutations()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        var mutation = cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "create"]
            });

        // Initially Idle
        var idleResult = cache.FindAll(new MutationFilters { Status = MutationStatus.Idle }).ToList();
        Assert.Single(idleResult);

        var pendingResult = cache.FindAll(new MutationFilters { Status = MutationStatus.Pending }).ToList();
        Assert.Empty(pendingResult);
    }

    [Fact]
    public void FindAll_WithPredicate()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos"]
            });
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["users"]
            });

        // Act
        var result = cache.FindAll(new MutationFilters
        {
            Predicate = m => m.MutationKey is not null &&
                             m.MutationKey.First()?.ToString() == "users"
        }).ToList();

        Assert.Single(result);
    }

    [Fact]
    public void Find_ReturnsFirstMatch()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos"]
            });

        var result = cache.Find(new MutationFilters { MutationKey = ["todos"] });
        Assert.NotNull(result);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoMatch()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        var result = cache.Find(new MutationFilters { MutationKey = ["nonexistent"] });
        Assert.Null(result);
    }

    [Fact]
    public void FindAll_ExactMatch_OnlyMatchesExact()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos"]
            });
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "create"]
            });

        // Act — exact match should only find ["todos"]
        var result = cache.FindAll(new MutationFilters
        {
            MutationKey = ["todos"],
            Exact = true
        }).ToList();

        Assert.Single(result);
    }

    [Fact]
    public void IsMutating_CountsPendingMutations()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        // Build two mutations — both start as Idle
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos"]
            });
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["users"]
            });

        // Act — no mutations are pending
        Assert.Equal(0, client.IsMutating());
    }

    [Fact]
    public void FindAll_NoKey_MatchesMutationsWithoutKey()
    {
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        // Mutation without a key
        cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?> { });

        // Act — no key filter should match all
        var result = cache.FindAll(null).ToList();
        Assert.Single(result);

        // With a key filter, mutations without keys should not match
        var filtered = cache.FindAll(new MutationFilters { MutationKey = ["todos"] }).ToList();
        Assert.Empty(filtered);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
