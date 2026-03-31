namespace RabstackQuery;

public class QueryDefaultsTests
{
    [Fact]
    public void SetQueryDefaults_AppliesGcTime_ToMatchingQueries()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000)
        });

        // Act — build a query that matches the prefix
        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1] });

        // Assert — the merged GcTime should be 10_000 from the key defaults
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), query.Options.GcTime);
    }

    [Fact]
    public void SetQueryDefaults_DoesNotApply_ToNonMatchingQueries()
    {
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000)
        });

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users", 1] });

        // Should get the default 5-minute GcTime, not 10_000
        Assert.Equal(QueryTimeDefaults.GcTime, query.Options.GcTime);
    }

    [Fact]
    public void SetQueryDefaults_PerQueryOverrides_KeyDefaults()
    {
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000)
        });

        var cache = client.QueryCache;

        // Per-query GcTime should win over key defaults
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMilliseconds(99_000) });

        Assert.Equal(TimeSpan.FromMilliseconds(99_000), query.Options.GcTime);
    }

    [Fact]
    public void SetQueryDefaults_MultipleDefaults_MergeInOrder()
    {
        var client = CreateQueryClient();

        // Register a broad default first
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000),
            Retry = 1
        });

        // Register a more specific default that overrides GcTime but not Retry
        client.SetQueryDefaults(["todos", 1], new QueryDefaults
        {
            QueryKey = ["todos", 1],
            GcTime = TimeSpan.FromMilliseconds(20_000)
        });

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1] });

        // GcTime comes from the more specific default (20_000)
        Assert.Equal(TimeSpan.FromMilliseconds(20_000), query.Options.GcTime);
        // Retry comes from the broader default (1) since the specific one didn't set it
        Assert.Equal(1, query.Options.Retry);
    }

    [Fact]
    public void GlobalDefaults_AppliedAsBaseline()
    {
        var client = CreateQueryClient();
        client.DefaultOptions = new QueryClientDefaultOptions
        {
            GcTime = TimeSpan.FromSeconds(60),
            Retry = 5
        };

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["anything"] });

        Assert.Equal(TimeSpan.FromSeconds(60), query.Options.GcTime);
        Assert.Equal(5, query.Options.Retry);
    }

    [Fact]
    public void GlobalDefaults_OverriddenByKeyDefaults()
    {
        var client = CreateQueryClient();
        client.DefaultOptions = new QueryClientDefaultOptions
        {
            GcTime = TimeSpan.FromSeconds(60),
            Retry = 5
        };

        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000)
        });

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"] });

        // GcTime from key defaults, Retry from global defaults
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), query.Options.GcTime);
        Assert.Equal(5, query.Options.Retry);
    }

    [Fact]
    public void SetQueryDefaults_ReplacesExisting_ForSameKey()
    {
        var client = CreateQueryClient();

        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000)
        });

        // Replace with new defaults for the same key
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(20_000)
        });

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"] });

        Assert.Equal(TimeSpan.FromMilliseconds(20_000), query.Options.GcTime);
    }

    [Fact]
    public void SetQueryDefaults_AppliesRetryDelay()
    {
        var client = CreateQueryClient();

        Func<int, Exception, TimeSpan> customDelay = (count, _) => TimeSpan.FromMilliseconds(count * 500);
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            RetryDelay = customDelay
        });

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"] });

        Assert.Same(customDelay, query.Options.RetryDelay);
    }

    [Fact]
    public void DefaultQueryOptions_Propagates_RefetchOnWindowFocus()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new QueryObserverOptions<string, string>
        {
            QueryKey = ["todos"],
            RefetchOnWindowFocus = RefetchOnBehavior.Never
        };

        // Act
        var resolved = client.DefaultQueryOptions(options);

        // Assert — the per-query value should survive the merge
        Assert.Equal(RefetchOnBehavior.Never, resolved.RefetchOnWindowFocus);
    }

    [Fact]
    public void DefaultQueryOptions_Propagates_RefetchOnReconnect()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new QueryObserverOptions<string, string>
        {
            QueryKey = ["todos"],
            RefetchOnReconnect = RefetchOnBehavior.Always
        };

        // Act
        var resolved = client.DefaultQueryOptions(options);

        // Assert
        Assert.Equal(RefetchOnBehavior.Always, resolved.RefetchOnReconnect);
    }

    [Fact]
    public void SetQueryDefaults_RefetchOnWindowFocus_Applied_To_Matching_Queries()
    {
        // Arrange — set per-key-prefix defaults for RefetchOnWindowFocus
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            RefetchOnWindowFocus = RefetchOnBehavior.Never
        });

        var options = new QueryObserverOptions<string, string>
        {
            QueryKey = ["todos", 1]
            // RefetchOnWindowFocus defaults to WhenStale, so key defaults should apply
        };

        // Act
        var resolved = client.DefaultQueryOptions(options);

        // Assert — key-level default should override the framework default
        Assert.Equal(RefetchOnBehavior.Never, resolved.RefetchOnWindowFocus);
    }

    [Fact]
    public void SetQueryDefaults_RefetchOnReconnect_Applied_To_Matching_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            RefetchOnReconnect = RefetchOnBehavior.Always
        });

        var options = new QueryObserverOptions<string, string>
        {
            QueryKey = ["todos", 1]
        };

        // Act
        var resolved = client.DefaultQueryOptions(options);

        // Assert
        Assert.Equal(RefetchOnBehavior.Always, resolved.RefetchOnReconnect);
    }

    [Fact]
    public void PerQuery_RefetchOnWindowFocus_Overrides_KeyDefaults()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            QueryKey = ["todos"],
            RefetchOnWindowFocus = RefetchOnBehavior.Always
        });

        var options = new QueryObserverOptions<string, string>
        {
            QueryKey = ["todos", 1],
            RefetchOnWindowFocus = RefetchOnBehavior.Never // explicit override
        };

        // Act
        var resolved = client.DefaultQueryOptions(options);

        // Assert — per-query value wins
        Assert.Equal(RefetchOnBehavior.Never, resolved.RefetchOnWindowFocus);
    }

    [Fact]
    public void DefaultOptions_Should_Apply_GcTime_To_Queries_Added_To_Cache()
    {
        // TanStack line 46: global GcTime default should be applied to all queries
        // Arrange
        var client = CreateQueryClient();
        client.DefaultOptions = new QueryClientDefaultOptions
        {
            GcTime = TimeSpan.FromMilliseconds(10_000)
        };

        // Act — create a query via SetQueryData (which builds the query through the cache)
        client.SetQueryData(["todos"], "data");

        // Verify by building through cache explicitly
        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["new-query"] });

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), query.Options.GcTime);
    }

    [Fact]
    public void DefaultOptions_Should_Return_Set_Options()
    {
        // TanStack line 61: getDefaultOptions should return the options set via setDefaultOptions
        // Arrange
        var client = CreateQueryClient();
        var options = new QueryClientDefaultOptions
        {
            StaleTime = TimeSpan.FromSeconds(30),
            GcTime = TimeSpan.FromMinutes(10),
            Retry = 5
        };

        // Act
        client.DefaultOptions = options;

        // Assert
        var result = client.DefaultOptions;
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(30), result!.StaleTime);
        Assert.Equal(TimeSpan.FromMinutes(10), result.GcTime);
        Assert.Equal(5, result.Retry);
    }

    [Fact]
    public void DefaultOptions_Should_Return_Null_When_Not_Set()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act & Assert
        Assert.Null(client.DefaultOptions);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
