namespace RabstackQuery;

/// <summary>
/// Tests for QueryClient orchestration: SetQueryData, GetQueryData, InvalidateQueriesAsync,
/// RefetchQueriesAsync, and CancelQueriesAsync.
/// </summary>
public sealed class QueryClientFullTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    #region SetQueryData

    [Fact]
    public void SetQueryData_Should_Not_Crash_When_Query_Not_Found()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act — setting data for a non-existent query should create it, not throw
        var exception = Record.Exception(() => client.SetQueryData(["nonexistent"], "data"));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void SetQueryData_Should_Create_Query_When_Not_Found()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        client.SetQueryData(["todos"], "seeded-data");

        // Assert
        var data = client.GetQueryData<string>(["todos"]);
        Assert.Equal("seeded-data", data);
    }

    [Fact]
    public void SetQueryData_Should_Update_Existing_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "initial");

        // Act
        client.SetQueryData(["todos"], "updated");

        // Assert
        var data = client.GetQueryData<string>(["todos"]);
        Assert.Equal("updated", data);
    }

    [Fact]
    public void SetQueryData_Should_Set_Status_To_Succeeded()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        client.SetQueryData(["todos"], "data");

        // Assert
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(queryHash);
        Assert.NotNull(query);
        Assert.Equal(QueryStatus.Succeeded, query!.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    [Fact]
    public void SetQueryData_With_Updater_Function_Should_Apply_Transform()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["count"], 10);

        // Act
        client.SetQueryData<int>(["count"], current => current + 5);

        // Assert
        var data = client.GetQueryData<int>(["count"]);
        Assert.Equal(15, data);
    }

    [Fact]
    public void SetQueryData_Should_Increment_DataUpdateCount()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "first");

        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(queryHash);
        var initialCount = query!.State!.DataUpdateCount;

        // Act
        client.SetQueryData(["todos"], "second");

        // Assert
        Assert.True(query.State!.DataUpdateCount > initialCount);
    }

    [Fact]
    public void SetQueryData_Should_Clear_Error_State()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");

        // Assert
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(queryHash);
        Assert.Null(query!.State!.Error);
        Assert.False(query.State.IsInvalidated);
    }

    #endregion

    #region GetQueryData

    [Fact]
    public void GetQueryData_Should_Return_Data_When_Query_Exists()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], new List<string> { "todo1", "todo2" });

        // Act
        var data = client.GetQueryData<List<string>>(["todos"]);

        // Assert
        Assert.NotNull(data);
        Assert.Equal(2, data.Count);
    }

    [Fact]
    public void GetQueryData_Should_Return_Default_When_Query_Not_Found()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var data = client.GetQueryData<string>(["nonexistent"]);

        // Assert
        Assert.Null(data);
    }

    [Fact]
    public void GetQueryData_Should_Return_Default_For_ValueType_When_Not_Found()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var data = client.GetQueryData<int>(["nonexistent"]);

        // Assert
        Assert.Equal(0, data);
    }

    #endregion

    #region InvalidateQueriesAsync

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Set_IsInvalidated_On_Matching_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);

        // Act
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert
        var query = client.QueryCache.Get<string>(queryHash);
        Assert.True(query!.State!.IsInvalidated);
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Crash_When_No_Matching_Query()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var exception = await Record.ExceptionAsync(() => client.InvalidateQueriesAsync(["nonexistent"]));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Trigger_Refetch_On_Active_Observers()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);
        var initialFetchCount = fetchCount;

        // Act
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert
        Assert.True(fetchCount > initialFetchCount, "Invalidation should trigger refetch");

        subscription.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Refetch_When_No_Observers()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);

        // Act
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert — the query should be invalidated but not refetching
        var query = client.QueryCache.Get<string>(queryHash);
        Assert.True(query!.State!.IsInvalidated);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    #endregion

    #region RefetchQueriesAsync

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_Matching_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Build the query and set its function
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var cache = client.QueryCache;
        var options = new QueryConfiguration<string>
        {
            QueryKey = ["todos"],
            QueryHash = queryHash,
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 0,
        };
        var query = cache.GetOrCreate<string, string>(client, options);
        query.SetQueryFn(async _ =>
        {
            fetchCount++;
            return $"data-v{fetchCount}";
        });

        await query.Fetch();
        Assert.Equal(1, fetchCount);

        // Act
        await client.RefetchQueriesAsync(["todos"]);

        // Assert
        Assert.Equal(2, fetchCount);
        Assert.Equal("data-v2", query.State!.Data);
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Do_Nothing_When_No_Matching_Query()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act — should not throw
        var exception = await Record.ExceptionAsync(() =>
            client.RefetchQueriesAsync(["nonexistent"]));

        // Assert
        Assert.Null(exception);
    }

    #endregion

    [Fact]
    public async Task SetQueryData_Should_Not_Reset_FetchStatus_During_Active_Fetch()
    {
        // TanStack's setQueryData preserves FetchStatus when an active fetch is in
        // progress. The previous implementation hardcoded FetchStatus.Idle, which
        // caused observers to briefly see a "not fetching" state mid-fetch.
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();
        var fetchStarted = new TaskCompletionSource();

        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["active-fetch"]);
        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["active-fetch"], GcTime = QueryTimeDefaults.GcTime, Retry = 0 });
        query.SetQueryFn(async _ =>
        {
            fetchStarted.TrySetResult();
            return await tcs.Task;
        });

        // Start an active fetch
        var fetchTask = query.Fetch();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FetchStatus.Fetching, query.State!.FetchStatus);

        // Act — set data while the fetch is still in progress
        client.SetQueryData(["active-fetch"], "optimistic-update");

        // Assert — FetchStatus should still be Fetching, not reset to Idle
        Assert.Equal(FetchStatus.Fetching, query.State!.FetchStatus);
        Assert.Equal("optimistic-update", query.State.Data);

        // Cleanup
        tcs.SetResult("done");
        await fetchTask;
    }

    [Fact]
    public void SetQueryData_Should_Not_Create_Query_When_Data_Is_Null()
    {
        // TanStack's setQueryData is a no-op when data is undefined for a nonexistent
        // query — it does not create a cache entry with null data.
        // Arrange
        var client = CreateQueryClient();

        // Act
        client.SetQueryData(["nonexistent"], (string?)null);

        // Assert — no query should have been created
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["nonexistent"]);
        var query = client.QueryCache.Get<string>(queryHash);
        Assert.Null(query);
    }

    [Fact]
    public void SetQueryData_Should_Not_Overwrite_Existing_Data_With_Null()
    {
        // TanStack's setQueryData ignores null updates on existing queries to
        // prevent accidental data loss.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "existing-data");

        // Act
        client.SetQueryData(["todos"], (string?)null);

        // Assert — data should remain unchanged
        var data = client.GetQueryData<string>(["todos"]);
        Assert.Equal("existing-data", data);
    }

    #region IsFetching tracking

    [Fact]
    public async Task IsFetching_Should_Return_Count_Of_Active_Fetches()
    {
        // TanStack line 396: IsFetching() should return the number of queries
        // with FetchStatus.Fetching, increasing and decreasing as fetches
        // start and complete.
        // Arrange
        var client = CreateQueryClient();
        var tcs1 = new TaskCompletionSource<string>();
        var tcs2 = new TaskCompletionSource<string>();
        var started1 = new TaskCompletionSource();
        var started2 = new TaskCompletionSource();

        Assert.Equal(0, client.IsFetching());

        // Act — start two prefetches that block on TCS gates

        var prefetch1 = client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["key1"],
            QueryFn = async _ =>
            {
                started1.TrySetResult();
                return await tcs1.Task;
            }
        });
        await started1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.IsFetching());

        var prefetch2 = client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["key2"],
            QueryFn = async _ =>
            {
                started2.TrySetResult();
                return await tcs2.Task;
            }
        });
        await started2.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, client.IsFetching());

        // Complete prefetch2 first
        tcs2.SetResult("data2");
        await prefetch2;
        Assert.Equal(1, client.IsFetching());

        // Complete prefetch1
        tcs1.SetResult("data1");
        await prefetch1;
        Assert.Equal(0, client.IsFetching());
    }

    [Fact]
    public async Task Query_IsFetching_Should_Be_True_During_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var isFetchingDuringFetch = false;
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["slow-query"],
                QueryFn = async _ => await tcs.Task,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            if (result.IsFetching)
            {
                isFetchingDuringFetch = true;
            }
        });

        // Wait for observer to start fetching
        await Task.Delay(50);

        // Assert — should be fetching while waiting
        Assert.True(isFetchingDuringFetch);

        // Cleanup
        tcs.SetResult("done");
        await Task.Delay(50);
        subscription.Dispose();
    }

    #endregion

    #region Clear

    [Fact]
    public async Task Clear_Should_Remove_All_Queries_And_Mutations()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data1");
        client.SetQueryData(["users"], "data2");

        // Add a mutation to the mutation cache via MutationObserver
        var mutationOptions = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "mutated"
        };
        var mutationObserver = new MutationObserver<string, Exception, string, object?>(client, mutationOptions);
        await mutationObserver.MutateAsync("input");

        // Verify data exists
        Assert.NotEmpty(client.QueryCache.FindAll());
        Assert.NotEmpty(client.MutationCache.FindAll());

        // Act
        client.Clear();

        // Assert
        Assert.Empty(client.QueryCache.FindAll());
        Assert.Empty(client.MutationCache.FindAll());
        Assert.Null(client.GetQueryData<string>(["todos"]));
        Assert.Null(client.GetQueryData<string>(["users"]));
    }

    [Fact]
    public void Clear_Should_Succeed_When_Caches_Are_Already_Empty()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act & Assert — should not throw
        var exception = Record.Exception(() => client.Clear());
        Assert.Null(exception);
    }

    #endregion

    #region Custom QueryKeyHasher

    [Fact]
    public async Task FetchQueryAsync_Should_Use_Custom_QueryKeyHasher()
    {
        // Arrange — a hasher that maps all keys to the same hash,
        // so two different keys share a single cache entry.
        var client = CreateQueryClient();
        var hasher = new ConstantHasher("same-hash");
        var fetchCount = 0;

        var options1 = new FetchQueryOptions<string>
        {
            QueryKey = ["key-a"],
            QueryFn = _ => { fetchCount++; return Task.FromResult("result-a"); },
            QueryKeyHasher = hasher,
        };

        var options2 = new FetchQueryOptions<string>
        {
            QueryKey = ["key-b"],
            QueryFn = _ => { fetchCount++; return Task.FromResult("result-b"); },
            QueryKeyHasher = hasher,
            // Data from first fetch is fresh (not stale) so second fetch is a cache hit
            StaleTime = TimeSpan.FromMinutes(5),
        };

        // Act
        var result1 = await client.FetchQueryAsync(options1);
        var result2 = await client.FetchQueryAsync(options2);

        // Assert — second fetch should hit cache because the custom hasher
        // made both keys resolve to the same hash
        Assert.Equal("result-a", result1);
        Assert.Equal("result-a", result2);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task QueryObserver_Should_Use_Custom_QueryKeyHasher()
    {
        // Arrange — a hasher where the observer's key produces a custom hash
        var client = CreateQueryClient();
        var hasher = new ConstantHasher("custom-observer-hash");
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["my-key"],
            QueryFn = _ => Task.FromResult("observer-data"),
            QueryKeyHasher = hasher,
        });

        // Act — subscribe to trigger the fetch
        var resultTcs = new TaskCompletionSource<IQueryResult<string>>();
        var sub = observer.Subscribe(result =>
        {
            if (result.Status is QueryStatus.Succeeded)
                resultTcs.TrySetResult(result);
        });

        var result = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — verify the query was stored under the custom hash
        var query = client.QueryCache.Get<string>("custom-observer-hash");
        Assert.NotNull(query);
        Assert.Equal("observer-data", result.Data);

        sub.Dispose();
    }

    /// <summary>
    /// A test hasher that always returns the same hash regardless of the key.
    /// </summary>
    private sealed class ConstantHasher(string hash) : IQueryKeyHasher
    {
        public string HashQueryKey(QueryKey queryKey) => hash;
    }

    #endregion

}
