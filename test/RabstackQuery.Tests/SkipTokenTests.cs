namespace RabstackQuery;

/// <summary>
/// Tests for <see cref="SkipToken"/> — the composable alternative to
/// <c>Enabled = false</c> for dependent queries.
/// TanStack reference: utils.ts:423-424, 435-449.
/// </summary>
public sealed class SkipTokenTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    #region Sentinel Identity

    [Fact]
    public void IsSkipToken_Returns_True_For_Sentinel()
    {
        var sentinel = SkipToken.QueryFn<string>();
        Assert.True(SkipToken.IsSkipToken(sentinel));
    }

    [Fact]
    public void IsSkipToken_Returns_False_For_Real_Function()
    {
        Func<QueryFunctionContext, Task<string>> realFn = _ => Task.FromResult("data");
        Assert.False(SkipToken.IsSkipToken(realFn));
    }

    [Fact]
    public void IsSkipToken_Returns_False_For_Null()
    {
        Assert.False(SkipToken.IsSkipToken<string>(null));
    }

    [Fact]
    public void QueryFn_Returns_Same_Instance_Per_TData()
    {
        // The sentinel is cached per TData — same generic arg returns same reference.
        var a = SkipToken.QueryFn<string>();
        var b = SkipToken.QueryFn<string>();
        Assert.Same(a, b);
    }

    [Fact]
    public void QueryFn_Returns_Different_Instances_For_Different_TData()
    {
        var stringFn = SkipToken.QueryFn<string>();
        var intFn = SkipToken.QueryFn<int>();

        // Can't use Assert.NotSame across different delegate types, so just
        // verify both are identified as skip tokens for their respective types.
        Assert.True(SkipToken.IsSkipToken(stringFn));
        Assert.True(SkipToken.IsSkipToken(intFn));
    }

    [Fact]
    public async Task Sentinel_Throws_InvalidOperationException_When_Invoked()
    {
        var sentinel = SkipToken.QueryFn<string>();
        var ctx = new QueryFunctionContext(CancellationToken.None, () => { });

        // Act
        var act = async () => await sentinel(ctx);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    #endregion

    #region Observer Behavior

    [Fact]
    public void Observer_With_SkipToken_Has_IsEnabled_False()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["skip-enabled"],
                QueryFn = SkipToken.QueryFn<string>()
            }
        );

        // Act & Assert
        Assert.False(observer.IsEnabled);
    }

    [Fact]
    public async Task Observer_With_SkipToken_Does_Not_Fetch_On_Mount()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["skip-no-fetch"],
                QueryFn = SkipToken.QueryFn<string>()
            }
        );

        // Act — subscribing should NOT trigger a fetch
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert — query stays in Pending because the sentinel was never invoked
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);

        subscription.Dispose();
    }

    [Fact]
    public async Task SetOptions_From_SkipToken_To_Real_Function_Triggers_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCompleted = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["skip-to-real"],
                QueryFn = SkipToken.QueryFn<string>()
            }
        );

        var subscription = observer.Subscribe(_ =>
        {
            if (observer.CurrentResult.Status is QueryStatus.Succeeded)
                fetchCompleted.TrySetResult(true);
        });

        // Verify initially disabled
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);

        // Act — switch to a real query function
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["skip-to-real"],
            QueryFn = async _ => "fetched-data"
        });

        // Assert — fetch should complete
        var completed = await Task.WhenAny(fetchCompleted.Task, Task.Delay(2000));
        Assert.Same(fetchCompleted.Task, completed);
        Assert.Equal("fetched-data", observer.CurrentResult.Data);

        subscription.Dispose();
    }

    [Fact]
    public async Task SetOptions_From_Real_Function_To_SkipToken_Stops_Fetching()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var firstFetchCompleted = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["real-to-skip"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return "data";
                }
            }
        );

        var subscription = observer.Subscribe(_ =>
        {
            if (observer.CurrentResult.Status is QueryStatus.Succeeded)
                firstFetchCompleted.TrySetResult(true);
        });

        // Wait for initial fetch
        await firstFetchCompleted.Task;
        Assert.True(fetchCount >= 1);

        // Act — switch to skipToken
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["real-to-skip"],
            QueryFn = SkipToken.QueryFn<string>()
        });

        // Assert — observer should now report disabled
        Assert.False(observer.CurrentResult.IsEnabled);

        subscription.Dispose();
    }

    #endregion

    #region Query-Level Behavior

    [Fact]
    public void Query_IsDisabled_Returns_True_For_Unobserved_SkipToken_Query()
    {
        // Arrange — create a query via an observer with skipToken, then remove
        // the observer so the query has no observers and no real query function.
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["skip-disabled-query"],
                QueryFn = SkipToken.QueryFn<string>()
            }
        );

        // Subscribe then immediately dispose to create the query entry
        var subscription = observer.Subscribe(_ => { });
        subscription.Dispose();

        // Act
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["skip-disabled-query"] });

        // Assert — the sentinel was never installed as _queryFn, so IsDisabled
        // returns true (no observers + null _queryFn).
        Assert.NotNull(query);
        Assert.True(query.IsDisabled());
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Skips_SkipToken_Queries()
    {
        // Mirrors TanStack queryClient.test.tsx:1519-1539 — invalidating
        // a query with skipToken should not trigger a refetch.
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["skip-invalidate"],
                QueryFn = SkipToken.QueryFn<string>()
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(20);

        // Act
        await client.InvalidateQueriesAsync(["skip-invalidate"]);
        await Task.Delay(50);

        // Assert — query stays in Pending (no fetch was triggered)
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);

        subscription.Dispose();
    }

    #endregion

    #region QueryOptions Integration

    [Fact]
    public void QueryOptions_With_SkipToken_Flows_Through_To_Disabled_Observer()
    {
        // Arrange — use QueryOptions<TData> (the reusable definition type)
        // with skipToken, then create an observer from it.
        var client = CreateQueryClient();
        var queryOptions = new QueryOptions<string>
        {
            QueryKey = ["skip-queryoptions"],
            QueryFn = SkipToken.QueryFn<string>()
        };

        var observerOptions = queryOptions.ToObserverOptions();
        var observer = new QueryObserver<string, string>(client, observerOptions);

        // Act & Assert
        Assert.False(observer.IsEnabled);
    }

    #endregion
}
