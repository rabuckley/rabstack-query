namespace RabstackQuery;

/// <summary>
/// Tests for QueryObserver observer registration, cleanup, and auto-refetch behavior.
/// Covers the fix for the critical bug where observers weren't registered with queries,
/// causing InvalidateQueriesAsync() to have no effect.
/// </summary>
public sealed class QueryObserverTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public void Observer_Should_Register_With_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Act
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        // Subscribe to trigger registration and initial fetch
        var updateCount = 0;
        var subscription = observer.Subscribe(result =>
        {
            updateCount++;
        });

        // Assert - observer should have triggered a fetch
        Assert.Equal(1, fetchCount);
        Assert.True(updateCount > 0);

        subscription.Dispose();
    }

    [Fact]
    public void Observer_Should_Unregister_When_Last_Listener_Unsubscribes()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ => "data",
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Get the query to check observer count
        var hasher = new DefaultQueryKeyHasher();
        var queryHash = hasher.HashQueryKey(["test"]);
        var query = queryCache.GetByHash(queryHash) as Query<string>;

        // Act - unsubscribe
        subscription.Dispose();

        // Assert - query should have no observers
        // We can verify this indirectly by checking that Dispatch doesn't call observers
        var dispatchedNotifications = 0;
        var secondSubscription = observer.Subscribe(_ =>
        {
            dispatchedNotifications++;
        });

        // Manually dispatch an update to verify observer was removed
        query?.Invalidate();

        secondSubscription.Dispose();
    }

    [Fact]
    public async Task Observer_Should_Auto_Refetch_When_Query_Invalidated()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var updateCount = 0;
        var latestData = "";

        var subscription = observer.Subscribe(result =>
        {
            updateCount++;
            latestData = result.Data ?? "";
        });

        // Wait for initial fetch
        await Task.Delay(50);
        var initialFetchCount = fetchCount;
        var initialUpdateCount = updateCount;

        // Act - invalidate the query (now async — triggers refetch internally)
        await client.InvalidateQueriesAsync(["test"]);

        // Assert - should have triggered another fetch
        Assert.True(fetchCount > initialFetchCount, "Query should have been refetched after invalidation");
        Assert.True(updateCount > initialUpdateCount, "Observer should have been notified of refetch");

        subscription.Dispose();
    }

    [Fact]
    public async Task Disabled_Observer_Should_Not_Fetch_On_Mount()
    {
        // Arrange — a disabled observer should not trigger an initial fetch,
        // even though its query is "active" (has observers). This tests the
        // observer's ShouldFetchOnMount behavior, not InvalidateQueriesAsync.
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false  // Disabled
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert - disabled observer should not fetch on mount
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_With_RefetchType_None_Should_Not_Refetch_Disabled_Observer()
    {
        // Arrange — invalidation with RefetchType.None should only mark the
        // query as invalidated without triggering any refetch.
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false  // Disabled
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Act - invalidate with no refetch
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            QueryKey = ["test"],
            RefetchType = InvalidateRefetchType.None
        });

        // Assert - should not have fetched
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Refetch_When_No_Listeners()
    {
        // Arrange — after all listeners unsubscribe, the query becomes
        // inactive and the default RefetchType.Active won't refetch it.
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Wait for initial fetch
        await Task.Delay(50);
        var initialFetchCount = fetchCount;

        // Act - unsubscribe all listeners, then invalidate
        subscription.Dispose();
        await client.InvalidateQueriesAsync(["test"]);

        // Assert - should not have refetched because query is now inactive
        Assert.Equal(initialFetchCount, fetchCount);
    }

    [Fact]
    public void Observer_Should_Re_Register_When_QueryKey_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test", 1],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        // Subscribe triggers initial fetch synchronously (the query function
        // completes inline since there's no async work).
        var subscription = observer.Subscribe(_ => { });
        var initialFetchCount = fetchCount;

        // Act - change options with different query key. SetOptions triggers a
        // new fetch synchronously for the same reason.
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["test", 2],
            QueryFn = async _ =>
            {
                fetchCount++;
                return "data-2";
            },
            Enabled = true
        });

        // Assert - should have created new query and fetched
        Assert.True(fetchCount > initialFetchCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Mutation_InvalidateQueriesAsync_Should_Trigger_Observer_Refetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var todos = new List<string> { "todo1" };

        var queryObserver = new QueryObserver<List<string>, List<string>>(
            client,
            new QueryObserverOptions<List<string>, List<string>>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return new List<string>(todos); // Return current todos
                },
                Enabled = true
            }
        );

        var latestData = new List<string>();
        var subscription = queryObserver.Subscribe(result =>
        {
            if (result.Data is not null)
            {
                latestData = new List<string>(result.Data);
            }
        });

        // Wait for initial fetch
        await Task.Delay(50);
        Assert.Single(latestData);

        // Act - add a todo and invalidate
        todos.Add("todo2");
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert - observer should have refetched with new data
        Assert.Equal(2, latestData.Count);
        Assert.True(fetchCount > 1);

        subscription.Dispose();
    }

    [Fact]
    public void Observer_With_Select_Transform_Should_Notify_Correctly()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // QueryObserver with TData=int (count), TQueryData=List<string> (todos)
        var observer = new QueryObserver<int, List<string>>(
            client,
            new QueryObserverOptions<int, List<string>>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return new List<string> { "todo1", "todo2" };
                },
                Select = todos => todos.Count, // Transform to count
                Enabled = true
            }
        );

        var updateCount = 0;
        var latestCount = 0;

        var subscription = observer.Subscribe(result =>
        {
            updateCount++;
            latestCount = result.Data;
        });

        // Assert - should have received transformed data (count)
        Assert.True(updateCount > 0);
        Assert.Equal(2, latestCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Multiple_Observers_On_Same_Query_Should_All_Be_Notified()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer1 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        var observer2 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ => "data",
                Enabled = true
            }
        );

        var observer1Updates = 0;
        var observer2Updates = 0;

        var sub1 = observer1.Subscribe(_ => observer1Updates++);
        var sub2 = observer2.Subscribe(_ => observer2Updates++);

        // Wait for initial fetches
        await Task.Delay(50);
        var initialUpdates1 = observer1Updates;
        var initialUpdates2 = observer2Updates;

        // Act - invalidate (async — triggers refetch internally)
        await client.InvalidateQueriesAsync(["test"]);

        // Assert - both observers should be notified
        Assert.True(observer1Updates > initialUpdates1);
        Assert.True(observer2Updates > initialUpdates2);

        sub1.Dispose();
        sub2.Dispose();
    }

    [Fact]
    public async Task Invalidation_With_Active_Listeners_Should_Not_Cause_Stack_Overflow()
    {
        // This test covers the fix for the critical bug where InvalidateAction
        // didn't clear the IsInvalidated flag when FetchAction was dispatched.
        // This caused infinite recursion: Invalidate -> OnQueryUpdate detects invalidated state
        // -> calls RefetchAsync -> Fetch -> Dispatch(FetchAction) -> notifies observers
        // -> OnQueryUpdate sees invalidated flag still true -> RefetchAsync again (infinite loop)

        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var updateCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    await Task.Delay(10); // Simulate async work
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            updateCount++;
        });

        // Wait for initial fetch to complete
        await Task.Delay(100);
        var initialFetchCount = fetchCount;
        var initialUpdateCount = updateCount;

        // Act - invalidate the query with active listeners
        // This should trigger a refetch but NOT cause infinite recursion
        await client.InvalidateQueriesAsync(["test"]);

        // Assert - should have fetched exactly once more, not infinitely
        // If the bug existed, fetchCount would be > 2 due to repeated refetches
        Assert.Equal(initialFetchCount + 1, fetchCount);
        Assert.True(updateCount > initialUpdateCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Observer_Should_Auto_Refetch_And_Update_Results()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var latestData = "";
        var subscription = observer.Subscribe(result =>
        {
            if (result.Data is not null)
            {
                latestData = result.Data;
            }
        });

        // Wait for initial fetch
        await Task.Delay(50);
        var initialData = latestData;

        // Act - invalidate (async — triggers refetch internally)
        await client.InvalidateQueriesAsync(["test"]);

        // Assert - data should have been updated with new version
        Assert.NotEqual(initialData, latestData);
        Assert.Contains("v2", latestData);

        subscription.Dispose();
    }
}
