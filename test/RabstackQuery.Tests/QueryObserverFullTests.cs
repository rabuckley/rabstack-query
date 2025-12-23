namespace RabstackQuery.Tests;

/// <summary>
/// Tests for QueryObserver subscription lifecycle, Select transforms,
/// Enabled behavior, SetOptions, and result state properties.
/// </summary>
public sealed class QueryObserverFullTests
{
    private static QueryClient CreateQueryClient(IFocusManager? focusManager = null)
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache, focusManager: focusManager);
    }

    #region Subscription

    [Fact]
    public async Task Subscribe_Should_Trigger_Fetch_When_Enabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["sub-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        // Act — subscribing triggers initial fetch when Enabled=true
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert
        Assert.True(fetchCount >= 1);

        subscription.Dispose();
    }

    [Fact]
    public void Subscribe_Should_Not_Trigger_Fetch_When_Disabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["sub-disabled-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });

        // Assert
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Subscribe_Should_Read_Latest_Data_After_Subscribing()
    {
        // Arrange
        var client = CreateQueryClient();
        string? receivedData = null;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["read-latest"],
                QueryFn = async _ => "fresh-data",
                Enabled = true
            }
        );

        // Act
        var subscription = observer.Subscribe(result =>
        {
            if (result.Data is not null)
                receivedData = result.Data;
        });
        await Task.Delay(50);

        // Assert
        Assert.Equal("fresh-data", receivedData);

        subscription.Dispose();
    }

    [Fact]
    public async Task Unsubscribe_Should_Stop_Receiving_Notifications()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var notificationCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["unsub-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => notificationCount++);
        await Task.Delay(50);
        var countAfterSubscribed = notificationCount;

        // Act — unsubscribe, then trigger an update
        subscription.Dispose();
        await client.InvalidateQueries(["unsub-test"]);
        await Task.Delay(100);

        // Assert — no new notifications after unsubscribe
        Assert.Equal(countAfterSubscribed, notificationCount);
    }

    #endregion

    #region Select Transform

    [Fact]
    public async Task Select_Should_Transform_TQueryData_To_TData()
    {
        // Arrange
        var client = CreateQueryClient();
        int? receivedCount = null;

        var observer = new QueryObserver<int, List<string>>(
            client,
            new QueryObserverOptions<int, List<string>>
            {
                QueryKey = ["select-test"],
                QueryFn = async _ => new List<string> { "a", "b", "c" },
                Select = list => list.Count,
                Enabled = true
            }
        );

        // Act
        var subscription = observer.Subscribe(result =>
        {
            if (result.Data != 0)
                receivedCount = result.Data;
        });
        await Task.Delay(50);

        // Assert
        Assert.Equal(3, receivedCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Select_Should_Return_Derived_Value()
    {
        // Arrange
        var client = CreateQueryClient();
        string? receivedFirst = null;

        var observer = new QueryObserver<string, List<string>>(
            client,
            new QueryObserverOptions<string, List<string>>
            {
                QueryKey = ["select-first"],
                QueryFn = async _ => new List<string> { "first", "second", "third" },
                Select = list => list.First(),
                Enabled = true
            }
        );

        // Act
        var subscription = observer.Subscribe(result =>
        {
            if (result.Data is not null)
                receivedFirst = result.Data;
        });
        await Task.Delay(50);

        // Assert
        Assert.Equal("first", receivedFirst);

        subscription.Dispose();
    }

    [Fact]
    public async Task Observer_Without_Select_Should_Pass_Data_Through()
    {
        // Arrange
        var client = CreateQueryClient();
        string? receivedData = null;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["no-select"],
                QueryFn = async _ => "pass-through",
                Enabled = true
            }
        );

        // Act
        var subscription = observer.Subscribe(result =>
        {
            if (result.Data is not null)
                receivedData = result.Data;
        });
        await Task.Delay(50);

        // Assert
        Assert.Equal("pass-through", receivedData);

        subscription.Dispose();
    }

    #endregion

    #region Enabled Behavior

    [Fact]
    public void Disabled_Observer_Should_Not_Fetch_On_Mount()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-mount"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });

        // Assert
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    [Fact]
    public async Task Disabled_Observer_Should_Not_Auto_Refetch_On_Invalidation()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Seed data so the query exists
        client.SetQueryData(["disabled-invalidate"], "initial");

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-invalidate"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Act
        await client.InvalidateQueries(["disabled-invalidate"]);
        await Task.Delay(100);

        // Assert
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    #endregion

    #region SetOptions

    [Fact]
    public async Task SetOptions_Changing_QueryKey_Should_Fetch_New_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchedKeys = new List<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["key-1"],
                QueryFn = async _ =>
                {
                    fetchedKeys.Add("key-1");
                    return "data-1";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Act — change query key; per TanStack, this triggers a fetch for the new key
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["key-2"],
            QueryFn = async _ =>
            {
                fetchedKeys.Add("key-2");
                return "data-2";
            },
            Enabled = true
        });
        await Task.Delay(50);

        // Assert — should have fetched both keys
        Assert.Contains("key-1", fetchedKeys);
        Assert.Contains("key-2", fetchedKeys);

        subscription.Dispose();
    }

    [Fact]
    public async Task SetOptions_Same_QueryKey_Should_Not_Re_Register()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["same-key"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);
        var countAfterInitial = fetchCount;

        // Act — set options with same key
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["same-key"],
            QueryFn = async _ =>
            {
                fetchCount++;
                return "data";
            },
            Enabled = true
        });
        await Task.Delay(50);

        // Assert — should not have re-fetched since key didn't change
        Assert.Equal(countAfterInitial, fetchCount);

        subscription.Dispose();
    }

    #endregion

    #region Result State Properties

    [Fact]
    public async Task IsLoading_Should_Be_True_During_Initial_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var isLoadingSeen = false;
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["loading-test"],
                QueryFn = async _ => await tcs.Task,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            // IsLoading = Status.Pending && FetchStatus.Fetching
            if (result.IsLoading)
                isLoadingSeen = true;
        });

        // Wait for the observer to start fetching
        await Task.Delay(50);

        // Assert — per TanStack, IsLoading is true on first fetch (Pending + Fetching)
        Assert.True(isLoadingSeen, "IsLoading should be true during initial fetch");

        // Cleanup
        tcs.SetResult("done");
        await Task.Delay(50);
        subscription.Dispose();
    }

    [Fact]
    public async Task IsSuccess_Should_Be_True_After_Successful_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["success-test"],
                QueryFn = async _ => "data",
                Enabled = true
            }
        );

        IQueryResult<string>? lastResult = null;
        var subscription = observer.Subscribe(result => lastResult = result);
        await Task.Delay(50);

        // Assert
        Assert.NotNull(lastResult);
        Assert.True(lastResult!.IsSuccess);
        Assert.Equal("data", lastResult.Data);

        subscription.Dispose();
    }

    [Fact]
    public async Task IsError_Should_Be_True_After_Failed_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();

        // Build a query with retry=0 so it fails immediately
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["error-test"]);
        var cache = client.GetQueryCache();
        var queryOptions = new QueryConfiguration<string>
        {
            QueryKey = ["error-test"],
            QueryHash = queryHash,
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 0
        };
        var query = cache.Build<string, string>(client, queryOptions);
        query.SetQueryFn(async _ => throw new InvalidOperationException("fail"));

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["error-test"],
                QueryFn = async _ => throw new InvalidOperationException("fail"),
                Enabled = false
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Act — trigger a fetch manually
        try { await query.Fetch(); } catch { }

        // Assert
        var result = observer.GetCurrentResult();
        Assert.True(result.IsError);
        Assert.NotNull(result.Error);

        subscription.Dispose();
    }

    [Fact]
    public async Task IsFetching_Should_Be_True_During_Background_Refetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var isFetchingDuringRefetch = false;
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    if (fetchCount == 1) return "initial";
                    return await tcs.Task;
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            // During background refetch, Status is still Succeeded from first fetch but IsFetching is true
            if (result.IsFetching && result.Data is not null)
                isFetchingDuringRefetch = true;
        });

        await Task.Delay(50); // Wait for initial fetch

        // Act — invalidate to trigger refetch. Don't await directly: the refetch
        // blocks on tcs.Task which is resolved below, so awaiting would deadlock.
        // The synchronous preamble of InvalidateQueries dispatches FetchAction,
        // which notifies the listener before the async refetch suspends.
        var invalidateTask = client.InvalidateQueries(["refetch-test"]);

        // Assert
        Assert.True(isFetchingDuringRefetch, "IsFetching should be true during refetch");

        // Cleanup — resolve the TCS so the refetch completes, then await
        tcs.SetResult("refetched");
        await invalidateTask;
        subscription.Dispose();
    }

    [Fact]
    public async Task IsStale_Should_Be_True_With_Default_StaleTime()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-default"],
                QueryFn = async _ => "data",
                Enabled = true,
                StaleTime = TimeSpan.Zero // Default: always stale
            }
        );

        IQueryResult<string>? lastResult = null;
        var subscription = observer.Subscribe(result => lastResult = result);
        await Task.Delay(50);

        // Assert
        Assert.NotNull(lastResult);
        Assert.True(lastResult!.IsStale, "Data should be stale with StaleTime=0");

        subscription.Dispose();
    }

    [Fact]
    public async Task IsStale_Should_Be_False_When_StaleTime_Not_Elapsed()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-long"],
                QueryFn = async _ => "data",
                Enabled = true,
                StaleTime = TimeSpan.FromSeconds(60) // 60 seconds — data should not be stale immediately
            }
        );

        IQueryResult<string>? lastResult = null;
        var subscription = observer.Subscribe(result =>
        {
            if (result.IsSuccess) lastResult = result;
        });
        await Task.Delay(50);

        // Assert
        Assert.NotNull(lastResult);
        Assert.False(lastResult!.IsStale, "Data should not be stale within StaleTime");

        subscription.Dispose();
    }

    [Fact]
    public void IsPending_Should_Be_True_Before_First_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["pending-test"],
                QueryFn = async _ => "data",
                Enabled = false // Don't auto-fetch
            }
        );

        // Act
        var result = observer.GetCurrentResult();

        // Assert — per TanStack, initial status is Pending when no data
        Assert.True(result.IsPending);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetCurrentResult_Should_Reflect_Latest_State()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["current-result"],
                QueryFn = async _ => "latest-data",
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Act
        var result = observer.GetCurrentResult();

        // Assert
        Assert.Equal("latest-data", result.Data);
        Assert.True(result.IsSuccess);

        subscription.Dispose();
    }

    #endregion

    #region IsFetchedAfterMount

    /// <summary>
    /// TanStack: isFetchedAfterMount should be false before any fetch completes
    /// after the observer attaches, even when IsFetched is also false.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void IsFetchedAfterMount_Should_Be_False_Before_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-1"],
                QueryFn = async _ => "data",
                Enabled = false
            });

        // Act
        var result = observer.GetCurrentResult();

        // Assert — no fetch has happened at all
        Assert.False(result.IsFetched);
        Assert.False(result.IsFetchedAfterMount);
    }

    /// <summary>
    /// TanStack: isFetchedAfterMount should be true after a fetch completes
    /// subsequent to the observer attaching. Mirrors queryObserver.ts:576–578.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task IsFetchedAfterMount_Should_Be_True_After_Fetch_Completes()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-2"],
                QueryFn = _ =>
                {
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                }
            });

        // Act — subscribe triggers fetch
        using var subscription = observer.Subscribe(_ => { });
        await fetched.Task;

        // Small delay for dispatch to propagate
        await Task.Delay(50);

        // Assert
        var result = observer.GetCurrentResult();
        Assert.True(result.IsFetched);
        Assert.True(result.IsFetchedAfterMount);
    }

    /// <summary>
    /// When the query already has cached data before the observer attaches,
    /// IsFetched should be true (the query has been fetched) but
    /// IsFetchedAfterMount should be false (no fetch completed since this
    /// observer attached). This is the key distinction between the two properties.
    /// Mirrors TanStack queryObserver.ts:576–578 baseline snapshot behavior.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task IsFetchedAfterMount_Should_Be_False_When_Using_Cached_Data()
    {
        // Arrange — populate the cache via a first observer
        var client = CreateQueryClient();
        var fetchCount = 0;
        var firstFetched = new TaskCompletionSource<bool>();

        var firstObserver = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-3"],
                QueryFn = _ =>
                {
                    fetchCount++;
                    firstFetched.TrySetResult(true);
                    return Task.FromResult("cached");
                },
                // Never stale so a second observer won't trigger a refetch
                StaleTime = Timeout.InfiniteTimeSpan
            });

        using var sub1 = firstObserver.Subscribe(_ => { });
        await firstFetched.Task;
        await Task.Delay(50);

        // Verify first observer sees the data
        Assert.True(firstObserver.GetCurrentResult().IsFetched);
        Assert.True(firstObserver.GetCurrentResult().IsFetchedAfterMount);

        // Act — second observer attaches to the same query key with cached data
        var secondObserver = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-3"],
                QueryFn = _ => Task.FromResult("cached"),
                StaleTime = Timeout.InfiniteTimeSpan
            });

        using var sub2 = secondObserver.Subscribe(_ => { });
        // Data is not stale, so no refetch fires
        await Task.Delay(50);

        var result = secondObserver.GetCurrentResult();

        // Assert — the query was fetched before, but not after this observer mounted
        Assert.True(result.IsFetched);
        Assert.False(result.IsFetchedAfterMount);
        Assert.Equal("cached", result.Data);
    }

    /// <summary>
    /// IsFetchedAfterMount should be true after an error fetch completes
    /// (not just successful fetches). Mirrors TanStack's errorUpdateCount
    /// comparison in queryObserver.ts:578.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task IsFetchedAfterMount_Should_Be_True_After_Error_Fetch()
    {
        // Arrange — pre-build the query with Retry=0 so the error completes immediately
        var client = CreateQueryClient();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["fetched-after-mount-err"]);
        var cache = client.GetQueryCache();
        cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["fetched-after-mount-err"],
            QueryHash = queryHash,
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 0
        });

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-err"],
                QueryFn = _ => throw new InvalidOperationException("boom")
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });
        await Task.Delay(100);

        // Assert
        var result = observer.GetCurrentResult();
        Assert.True(result.IsFetched);
        Assert.True(result.IsFetchedAfterMount);
        Assert.True(result.IsError);
    }

    /// <summary>
    /// After a key change via SetOptions, IsFetchedAfterMount should reset
    /// because the observer reattaches to a new query with a fresh baseline
    /// snapshot. The new query hasn't been fetched yet from this observer's
    /// perspective.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task IsFetchedAfterMount_Should_Reset_After_Key_Change()
    {
        // Arrange — fetch on key A
        var client = CreateQueryClient();
        var fetchedA = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["fetched-after-mount-keyA"],
                QueryFn = _ =>
                {
                    fetchedA.TrySetResult(true);
                    return Task.FromResult("dataA");
                }
            });

        using var subscription = observer.Subscribe(_ => { });
        await fetchedA.Task;
        await Task.Delay(50);

        Assert.True(observer.GetCurrentResult().IsFetchedAfterMount);

        // Act — change key, which rebinds the observer to a new query
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["fetched-after-mount-keyB"],
            QueryFn = _ => Task.FromResult("dataB"),
            Enabled = false // Prevent auto-fetch so we can observe the pre-fetch state
        });

        // Assert — observer is now on a fresh query with no fetches yet
        var result = observer.GetCurrentResult();
        Assert.False(result.IsFetchedAfterMount);
    }

    #endregion

    #region RefetchAsync

    [Fact]
    public async Task RefetchAsync_Should_Return_Updated_Result()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-async"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = false
            }
        );

        // Act
        var result = await observer.RefetchAsync();

        // Assert
        Assert.Equal("data-v1", result.Data);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RefetchAsync_Should_Return_Fresh_Data_On_Second_Call()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-fresh"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = false
            }
        );

        // Act
        var result1 = await observer.RefetchAsync();
        var result2 = await observer.RefetchAsync();

        // Assert
        Assert.Equal("data-v1", result1.Data);
        Assert.Equal("data-v2", result2.Data);
    }

    /// <summary>
    /// Mirrors TanStack: result.refetch() triggers a new fetch and returns updated data.
    /// The refetch delegate is bound to the observer that created the result, so consumers
    /// can trigger fetches without holding an observer reference.
    /// </summary>
    [Fact]
    public async Task Result_RefetchAsync_Should_Trigger_Fetch_And_Return_Updated_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["result-refetch"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = false
            }
        );

        // Act — first fetch through observer, then refetch through result
        var result1 = await observer.RefetchAsync();
        var result2 = await result1.RefetchAsync();

        // Assert
        Assert.Equal("data-v1", result1.Data);
        Assert.Equal("data-v2", result2.Data);
        Assert.True(result2.IsSuccess);
    }

    /// <summary>
    /// Successive calls to result.RefetchAsync() should each return fresh data,
    /// following the chain: result1 → result2 → result3.
    /// </summary>
    [Fact]
    public async Task Result_RefetchAsync_Should_Chain_Through_Multiple_Results()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["result-refetch-chain"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = false
            }
        );

        // Act
        var result1 = await observer.RefetchAsync();
        var result2 = await result1.RefetchAsync();
        var result3 = await result2.RefetchAsync();

        // Assert — each result in the chain has progressively newer data
        Assert.Equal("data-v1", result1.Data);
        Assert.Equal("data-v2", result2.Data);
        Assert.Equal("data-v3", result3.Data);
    }

    /// <summary>
    /// result.RefetchAsync(ThrowOnError: true) should propagate errors to the caller,
    /// matching TanStack's throwOnError option on RefetchOptions.
    /// </summary>
    [Fact]
    public async Task Result_RefetchAsync_With_ThrowOnError_Should_Propagate_Errors()
    {
        // Arrange — pre-build query with Retry=0 so errors fail immediately
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["result-refetch-throw"]);
        var queryOptions = new QueryConfiguration<string>
        {
            QueryKey = ["result-refetch-throw"],
            QueryHash = queryHash,
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 0
        };
        var query = cache.Build<string, string>(client, queryOptions);
        query.SetQueryFn(_ => throw new InvalidOperationException("fetch failed"));

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["result-refetch-throw"],
                Enabled = false
            }
        );

        // Act — get initial result, then refetch through it
        var result = await observer.RefetchAsync();

        // Assert — refetch through result with ThrowOnError propagates the exception
        Func<Task> act = () => result.RefetchAsync(new RefetchOptions { ThrowOnError = true });
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    /// <summary>
    /// result.RefetchAsync() without ThrowOnError should suppress errors and return
    /// the error state in the result, matching TanStack's default behavior.
    /// </summary>
    [Fact]
    public async Task Result_RefetchAsync_Without_ThrowOnError_Should_Suppress_Errors()
    {
        // Arrange — pre-build query with Retry=0 so errors fail immediately
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["result-refetch-suppress"]);
        var queryOptions = new QueryConfiguration<string>
        {
            QueryKey = ["result-refetch-suppress"],
            QueryHash = queryHash,
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 0
        };
        var query = cache.Build<string, string>(client, queryOptions);
        query.SetQueryFn(_ => throw new InvalidOperationException("fetch failed"));

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["result-refetch-suppress"],
                Enabled = false
            }
        );

        // Act — refetch through result without ThrowOnError
        var result1 = await observer.RefetchAsync();
        var result2 = await result1.RefetchAsync();

        // Assert — error is captured in the result, not thrown
        Assert.True(result2.IsError);
        Assert.IsType<InvalidOperationException>(result2.Error);
    }

    /// <summary>
    /// The initial result from GetCurrentResult() (before any fetch) should also
    /// have a working RefetchAsync, since the observer binds the delegate on all
    /// results including the initial pending state.
    /// </summary>
    [Fact]
    public async Task GetCurrentResult_RefetchAsync_Should_Work_On_Initial_Result()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["initial-result-refetch"],
                QueryFn = async _ => "fetched-data",
                Enabled = false
            }
        );

        // Act — get the initial (pending) result and refetch from it
        var initialResult = observer.GetCurrentResult();
        Assert.True(initialResult.IsPending);

        var fetchedResult = await initialResult.RefetchAsync();

        // Assert
        Assert.Equal("fetched-data", fetchedResult.Data);
        Assert.True(fetchedResult.IsSuccess);
    }

    #endregion

    #region Cached Data on Subscribe (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should be able to read latest data after subscribing"
    /// Pre-populates cache with setQueryData, then subscribes a disabled observer.
    /// The observer should immediately see the cached data without fetching.
    /// </summary>
    [Fact]
    public void Subscribe_Should_Read_Cached_Data_Without_Fetching()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["cached-read"], "cached-value");

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["cached-read"],
                Enabled = false
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        var result = observer.GetCurrentResult();

        // Assert — observer should see cached data immediately
        Assert.Equal(QueryStatus.Succeeded, result.Status);
        Assert.Equal("cached-value", result.Data);

        subscription.Dispose();
    }

    /// <summary>
    /// Mirrors TanStack: "should not trigger a fetch when not subscribed"
    /// Creating an observer without subscribing should not trigger a fetch.
    /// </summary>
    [Fact]
    public async Task Constructor_Should_Not_Trigger_Fetch_Without_Subscription()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Act — create observer but don't subscribe
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["no-sub-fetch"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = true
            }
        );

        await Task.Delay(50);

        // Assert — no fetch without subscription
        Assert.Equal(0, fetchCount);
    }

    /// <summary>
    /// Mirrors TanStack: "should be able to read latest data when re-subscribing (but not re-fetching)"
    /// After data arrives, unsubscribing and re-subscribing should return cached data
    /// without triggering a new fetch (when staleTime is long).
    /// </summary>
    [Fact]
    public async Task Resubscribe_Should_Read_Cached_Data_Without_Refetching()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["resub-cached"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    await Task.Delay(10);
                    return "data";
                },
                Enabled = true,
                StaleTime = TimeSpan.FromSeconds(60) // Data stays fresh for 60s
            }
        );

        // Subscribe and wait for initial fetch
        var sub1 = observer.Subscribe(_ => { });
        await Task.Delay(50);
        Assert.Equal(1, fetchCount);
        Assert.Equal("data", observer.GetCurrentResult().Data);

        // Unsubscribe
        sub1.Dispose();

        // Re-subscribe — should read cached data without refetching
        var sub2 = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert
        Assert.Equal(1, fetchCount); // No new fetch
        Assert.Equal("data", observer.GetCurrentResult().Data);

        sub2.Dispose();
    }

    #endregion

    #region Watch Without QueryFn (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should be able to watch a query without defining a query function"
    /// A disabled observer with no queryFn watches for external data changes (e.g., fetchQuery).
    /// </summary>
    [Fact]
    public async Task Watch_Without_QueryFn_Should_See_External_FetchQuery()
    {
        // Arrange
        var client = CreateQueryClient();
        var callbackCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["watch-external"],
                Enabled = false
            }
        );

        var subscription = observer.Subscribe(_ => callbackCount++);

        // Act — fetch data externally
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["watch-external"],
            QueryFn = async _ => "external-data"
        });
        await Task.Delay(50);

        // Assert — observer should have been notified
        Assert.True(callbackCount >= 2, $"Expected at least 2 callbacks, got {callbackCount}");
        Assert.Equal("external-data", observer.GetCurrentResult().Data);

        subscription.Dispose();
    }

    #endregion

    #region Multiple Subscribers on Same Observer (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should be able to handle multiple subscribers"
    /// Multiple callbacks on the same observer should all receive updates.
    /// </summary>
    [Fact]
    public async Task Multiple_Subscribers_On_Same_Observer_Should_All_Receive_Updates()
    {
        // Arrange
        var client = CreateQueryClient();
        var results1 = new List<string?>();
        var results2 = new List<string?>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["multi-sub"],
                Enabled = false
            }
        );

        var sub1 = observer.Subscribe(r => results1.Add(r.Data));
        var sub2 = observer.Subscribe(r => results2.Add(r.Data));

        // Act — fetch data externally
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["multi-sub"],
            QueryFn = async _ => "shared-data"
        });
        await Task.Delay(50);

        // Assert — both subscribers got the data
        Assert.Contains("shared-data", results1);
        Assert.Contains("shared-data", results2);

        sub1.Dispose();
        sub2.Dispose();
    }

    #endregion

    #region Retry Stops on Unsubscribe (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should stop retry when unsubscribing"
    /// After unsubscribing, retry loop should stop.
    /// </summary>
    [Fact]
    public async Task Retry_Should_Stop_When_Unsubscribing()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["retry-stop"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    throw new InvalidOperationException("always fails");
                },
                Enabled = true
            }
        );

        // Act — subscribe to start fetch + retries
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(100);
        var countAfterFirstRetries = fetchCount;

        // Unsubscribe to stop retries
        subscription.Dispose();
        await Task.Delay(200);

        // Assert — retry count should not have increased after unsubscribe
        Assert.Equal(countAfterFirstRetries, fetchCount);
    }

    #endregion

    #region Disabled Observer Staleness (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "disabled observers should not be stale"
    /// A disabled observer with no data should report IsStale = false.
    /// </summary>
    [Fact]
    public void Disabled_Observer_Should_Not_Be_Stale()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-stale"],
                Enabled = false
            }
        );

        // Act
        var result = observer.GetCurrentResult();

        // Assert — TanStack: disabled observers should not be stale
        Assert.False(result.IsStale);
    }

    #endregion

    #region Select with Refetch (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should run the selector again if the data changed"
    /// When data changes between refetches, the selector should re-run.
    /// </summary>
    [Fact]
    public async Task Select_Should_Rerun_When_Data_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var selectCount = 0;

        var observer = new QueryObserver<int, string>(
            client,
            new QueryObserverOptions<int, string>
            {
                QueryKey = ["select-rerun"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-{fetchCount}";
                },
                Select = data =>
                {
                    selectCount++;
                    return data.Length;
                },
                Enabled = false
            }
        );

        // Act
        var result1 = await observer.RefetchAsync();
        var result2 = await observer.RefetchAsync();

        // Assert — selector ran twice because data changed
        Assert.Equal(2, selectCount);
        Assert.Equal("data-1".Length, result1.Data);
        Assert.Equal("data-2".Length, result2.Data);
    }

    /// <summary>
    /// Mirrors TanStack: "should be able to fetch with a selector using the fetch method"
    /// RefetchAsync should return data transformed by the selector.
    /// </summary>
    [Fact]
    public async Task RefetchAsync_Should_Apply_Select_Transform()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<int, List<int>>(
            client,
            new QueryObserverOptions<int, List<int>>
            {
                QueryKey = ["select-refetch"],
                QueryFn = async _ => new List<int> { 1, 2, 3 },
                Select = list => list.Count,
                Enabled = false
            }
        );

        // Act
        var result = await observer.RefetchAsync();

        // Assert
        Assert.Equal(3, result.Data);
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Notify When Switching Query (ported from TanStack queryObserver.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should notify when switching query"
    /// setOptions with a different queryKey should notify subscribers with state transitions.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Notify_When_Switching_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new List<(string? Data, QueryStatus Status)>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["switch-1"],
                QueryFn = async _ => "value-1",
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            results.Add((result.Data, result.Status));
        });

        await Task.Delay(50); // Wait for first query to complete

        // Act — switch to a different query key
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["switch-2"],
            QueryFn = async _ => "value-2",
            Enabled = true
        });

        await Task.Delay(50); // Wait for second query to complete

        // Assert — should have seen transitions for both queries
        Assert.Contains(results, r => r.Data == "value-1" && r.Status == QueryStatus.Succeeded);
        Assert.Contains(results, r => r.Data == "value-2" && r.Status == QueryStatus.Succeeded);

        subscription.Dispose();
    }

    #endregion

    #region Unimplemented Features — Skip Markers

    /// <summary>
    /// TanStack: "enabled is a callback that initially returns false" — should not fetch on mount.
    /// Mirrors the three scenarios from TanStack's queryObserver test:
    /// 1. <c>EnabledFn</c> returning false prevents fetch on subscribe
    /// 2. <c>SetOptions</c> with <c>EnabledFn</c> returning true triggers fetch
    /// 3. Dependent query pattern: <c>EnabledFn</c> receives the query and
    ///    returns a state-dependent value
    /// </summary>
    [Fact]
    public async Task EnabledFn_Should_Prevent_Fetch_When_Returns_False()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["enabled-fn-false"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                EnabledFn = _ => false
            }
        );

        // Act — subscribing should not trigger a fetch
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert
        Assert.Equal(0, fetchCount);
        Assert.False(observer.IsEnabled);

        subscription.Dispose();
    }

    /// <summary>
    /// Switching <c>EnabledFn</c> from false → true via <c>SetOptions</c>
    /// triggers a fetch for active listeners.
    /// </summary>
    [Fact]
    public async Task EnabledFn_SetOptions_True_Should_Trigger_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var fetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["enabled-fn-toggle"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) fetchDone.Release();
                    return "data";
                },
                EnabledFn = _ => false
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);
        Assert.Equal(0, fetchCount);

        // Act — flip EnabledFn to return true
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["enabled-fn-toggle"],
            QueryFn = async _ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                if (count == 1) fetchDone.Release();
                return "data";
            },
            EnabledFn = _ => true
        });

        await fetchDone.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 1);
        Assert.True(observer.IsEnabled);

        subscription.Dispose();
    }

    /// <summary>
    /// Dependent query pattern: <c>EnabledFn</c> gates the observer based on
    /// external state. Initially the callback returns false (dependency not
    /// ready). After the dependency is satisfied, <c>SetOptions</c> provides
    /// a new callback returning true, triggering the fetch.
    /// </summary>
    [Fact]
    public async Task EnabledFn_Dependent_Query_Pattern()
    {
        // Arrange
        var client = CreateQueryClient();
        var postsFetched = false;
        var fetchDone = new SemaphoreSlim(0, 1);

        // Initially the "user" dependency is absent, so EnabledFn returns false.
        var postsObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["posts"],
                QueryFn = async _ =>
                {
                    postsFetched = true;
                    fetchDone.Release();
                    return "post-list";
                },
                EnabledFn = _ => false
            }
        );

        var subscription = postsObserver.Subscribe(_ => { });
        await Task.Delay(50);

        // No user data yet — posts should not have fetched
        Assert.False(postsFetched);

        // Act — dependency satisfied: swap to a callback that returns true.
        // In a real app this would be triggered by the user query resolving,
        // causing the component to re-render with a new EnabledFn closure.
        postsObserver.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["posts"],
            QueryFn = async _ =>
            {
                postsFetched = true;
                fetchDone.Release();
                return "post-list";
            },
            EnabledFn = _ => true
        });

        await fetchDone.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — now enabled, should have fetched
        Assert.True(postsFetched);
        Assert.Equal("post-list", postsObserver.GetCurrentResult().Data);

        subscription.Dispose();
    }

    /// <summary>
    /// <c>EnabledFn</c> takes precedence over the static <c>Enabled</c> property.
    /// Even when <c>Enabled = false</c>, <c>EnabledFn = _ => true</c> wins.
    /// </summary>
    [Fact]
    public async Task EnabledFn_Should_Take_Precedence_Over_Static_Enabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["enabled-fn-precedence"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return "data";
                },
                Enabled = false,
                EnabledFn = _ => true
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert — EnabledFn wins, fetch occurred
        Assert.True(observer.IsEnabled);
        Assert.True(fetchCount >= 1);

        subscription.Dispose();
    }

    /// <summary>
    /// Disabled observer via <c>EnabledFn</c> should report <c>IsStale = false</c>,
    /// matching the static <c>Enabled = false</c> behavior.
    /// </summary>
    [Fact]
    public void EnabledFn_Disabled_Observer_Should_Not_Be_Stale()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["enabled-fn-stale"],
                EnabledFn = _ => false
            }
        );

        // Act
        var result = observer.GetCurrentResult();

        // Assert — disabled observers should not be stale, regardless of mechanism
        Assert.False(result.IsStale);
        Assert.False(result.IsEnabled);
    }

    /// <summary>
    /// TanStack: "uses placeholderData as non-cache data when pending a query with no data"
    /// Static placeholder shows synthetic data while the query is pending, then gets
    /// replaced by real data when the fetch completes.
    /// </summary>
    [Fact]
    public async Task PlaceholderData_Static_Should_Show_Synthetic_Data_While_Pending()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();
        var results = new List<(string? Data, bool IsPlaceholder, bool IsSuccess, bool IsPending)>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["placeholder-static"],
                QueryFn = async _ => await tcs.Task,
                PlaceholderData = QueryUtilities.Of("loading…"),
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            results.Add((result.Data, result.IsPlaceholderData, result.IsSuccess, result.IsPending));
        });

        // Allow the observer to start fetching and compute the placeholder result
        await Task.Delay(50);

        // Assert — placeholder phase: IsPlaceholderData=true, status overridden to Succeeded
        var placeholderResult = observer.GetCurrentResult();
        Assert.Equal("loading…", placeholderResult.Data);
        Assert.True(placeholderResult.IsPlaceholderData);
        Assert.True(placeholderResult.IsSuccess);
        Assert.False(placeholderResult.IsPending);

        // Act — complete the real fetch
        tcs.SetResult("real-data");
        await Task.Delay(50);

        // Assert — real data replaces placeholder
        var realResult = observer.GetCurrentResult();
        Assert.Equal("real-data", realResult.Data);
        Assert.False(realResult.IsPlaceholderData);
        Assert.True(realResult.IsSuccess);

        subscription.Dispose();
    }

    /// <summary>
    /// Placeholder is not used when the cache already has data — the existing
    /// cached data takes precedence.
    /// </summary>
    [Fact]
    public void PlaceholderData_Should_Not_Be_Used_When_Cache_Has_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["placeholder-cached"], "cached-value");

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["placeholder-cached"],
                PlaceholderData = QueryUtilities.Of("placeholder"),
                Enabled = false
            }
        );

        // Act
        var result = observer.GetCurrentResult();

        // Assert — cache hit, no placeholder
        Assert.Equal("cached-value", result.Data);
        Assert.False(result.IsPlaceholderData);
    }

    /// <summary>
    /// KeepPreviousData passes the previous query's data when switching keys,
    /// enabling smooth pagination UX.
    /// </summary>
    [Fact]
    public async Task KeepPreviousData_Should_Show_Previous_Query_Data_On_Key_Change()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["page", "1"],
                QueryFn = async _ => "page-1-data",
                PlaceholderData = QueryUtilities.KeepPreviousData<string>,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Verify first page loaded
        Assert.Equal("page-1-data", observer.GetCurrentResult().Data);
        Assert.False(observer.GetCurrentResult().IsPlaceholderData);

        // Act — switch to page 2 with a slow fetch
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["page", "2"],
            QueryFn = async _ => await tcs.Task,
            PlaceholderData = QueryUtilities.KeepPreviousData<string>,
            Enabled = true
        });
        await Task.Delay(50);

        // Assert — previous data shown as placeholder while page 2 loads
        var midResult = observer.GetCurrentResult();
        Assert.Equal("page-1-data", midResult.Data);
        Assert.True(midResult.IsPlaceholderData);

        // Complete page 2 fetch
        tcs.SetResult("page-2-data");
        await Task.Delay(50);

        var finalResult = observer.GetCurrentResult();
        Assert.Equal("page-2-data", finalResult.Data);
        Assert.False(finalResult.IsPlaceholderData);

        subscription.Dispose();
    }

    /// <summary>
    /// Placeholder data flows through the Select transform before being
    /// returned in the result.
    /// </summary>
    [Fact]
    public async Task PlaceholderData_Should_Flow_Through_Select_Transform()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<List<string>>();

        var observer = new QueryObserver<int, List<string>>(
            client,
            new QueryObserverOptions<int, List<string>>
            {
                QueryKey = ["placeholder-select"],
                QueryFn = async _ => await tcs.Task,
                PlaceholderData = QueryUtilities.Of(new List<string> { "a", "b" }),
                Select = list => list.Count,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert — placeholder data went through Select
        var result = observer.GetCurrentResult();
        Assert.Equal(2, result.Data);
        Assert.True(result.IsPlaceholderData);

        // Cleanup
        tcs.SetResult(["a", "b", "c"]);
        await Task.Delay(50);

        var finalResult = observer.GetCurrentResult();
        Assert.Equal(3, finalResult.Data);
        Assert.False(finalResult.IsPlaceholderData);

        subscription.Dispose();
    }

    /// <summary>
    /// When the placeholder function returns null, placeholder data is not
    /// activated — the observer stays in Pending status.
    /// </summary>
    [Fact]
    public async Task PlaceholderData_Returning_Null_Should_Not_Activate()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["placeholder-null"],
                QueryFn = async _ => await tcs.Task,
                PlaceholderData = (_, _) => null,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Assert — null placeholder means no activation
        var result = observer.GetCurrentResult();
        Assert.False(result.IsPlaceholderData);
        Assert.True(result.IsPending);
        Assert.Null(result.Data);

        tcs.SetResult("data");
        subscription.Dispose();
    }

    /// <summary>
    /// Memoization: when the PlaceholderData delegate reference is unchanged
    /// across updates, the observer reuses the previous placeholder result and
    /// skips re-running Select.
    /// </summary>
    [Fact(Timeout = 10 * 1000)]
    public async Task PlaceholderData_Memoization_Should_Skip_Select_Rerun()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<List<string>>();
        var selectCount = 0;

        var placeholderFn = QueryUtilities.Of(new List<string> { "x" });

        var observer = new QueryObserver<int, List<string>>(
            client,
            new QueryObserverOptions<int, List<string>>
            {
                QueryKey = ["placeholder-memo"],
                QueryFn = async _ => await tcs.Task,
                PlaceholderData = placeholderFn,
                Select = list =>
                {
                    selectCount++;
                    return list.Count;
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var selectCountAfterFirst = selectCount;

        // Act — trigger another update (e.g., from external event) while still pending.
        // The same placeholder delegate should be memoized. Use RefetchType.None to
        // avoid deadlocking: the in-flight fetch awaits `tcs.Task`, and a refetch
        // would deduplicate to the same task, blocking InvalidateQueries forever.
        await client.InvalidateQueries(
            new InvalidateQueryFilters { QueryKey = ["placeholder-memo"], RefetchType = InvalidateRefetchType.None },
            TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert — Select should not have been called again for the memoized placeholder
        Assert.Equal(selectCountAfterFirst, selectCount);

        tcs.SetResult(["a", "b", "c"]);
        subscription.Dispose();
    }

    /// <summary>
    /// Mirrors TanStack: "should clear interval when unsubscribing to a refetchInterval query"
    /// Verifies that polling fires repeatedly and stops when the subscription is disposed.
    /// </summary>
    [Fact]
    public async Task RefetchInterval_Should_Poll_And_Stop_On_Unsubscribe()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var secondFetchReached = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-test"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count >= 2) secondFetchReached.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchInterval = TimeSpan.FromMilliseconds(50)
            }
        );

        // Act — subscribe to start polling
        var subscription = observer.Subscribe(_ => { });

        // Wait for at least two fetches (initial + one poll tick)
        await secondFetchReached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fetchCount >= 2, $"Expected at least 2 fetches, got {fetchCount}");

        var countAtUnsubscribe = fetchCount;

        // Unsubscribe — polling should stop
        subscription.Dispose();
        await Task.Delay(200);

        // Assert — no additional fetches after unsubscribe
        Assert.Equal(countAtUnsubscribe, fetchCount);
    }

    /// <summary>
    /// Verifies that polling pauses when the application is unfocused
    /// (default <c>RefetchIntervalInBackground = false</c>).
    /// </summary>
    [Fact]
    public async Task RefetchIntervalInBackground_False_Should_Not_Poll_When_Unfocused()
    {
        // Arrange — isolated FocusManager avoids cross-test interference
        var focusManager = new FocusManager();
        var client = CreateQueryClient(focusManager: focusManager);
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-unfocused"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetchDone.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchInterval = TimeSpan.FromMilliseconds(50),
                RefetchIntervalInBackground = false
            }
        );

        // Unfocus the app before subscribing
        focusManager.SetFocused(false);

        var subscription = observer.Subscribe(_ => { });

        // Wait for the initial mount fetch
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        var countAfterInitial = fetchCount;

        // Wait long enough for several poll ticks — they should all be skipped
        await Task.Delay(200);

        // Assert — no additional fetches while unfocused
        Assert.Equal(countAfterInitial, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <c>RefetchIntervalInBackground = true</c> keeps polling
    /// even when the application is unfocused.
    /// </summary>
    [Fact]
    public async Task RefetchIntervalInBackground_True_Should_Poll_When_Unfocused()
    {
        // Arrange — isolated FocusManager avoids cross-test interference
        var focusManager = new FocusManager();
        var client = CreateQueryClient(focusManager: focusManager);
        var fetchCount = 0;
        var secondFetchReached = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-unfocused-bg"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count >= 2) secondFetchReached.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchInterval = TimeSpan.FromMilliseconds(50),
                RefetchIntervalInBackground = true
            }
        );

        focusManager.SetFocused(false);

        var subscription = observer.Subscribe(_ => { });

        // Even though unfocused, polling should proceed because RefetchIntervalInBackground = true
        await secondFetchReached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fetchCount >= 2, $"Expected at least 2 fetches while unfocused, got {fetchCount}");

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <c>RefetchInterval = 0</c> means no polling — only the
    /// initial mount fetch occurs.
    /// </summary>
    [Fact]
    public async Task RefetchInterval_Zero_Should_Not_Poll()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-zero"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetchDone.Release();
                    return "data";
                },
                Enabled = true,
                RefetchInterval = TimeSpan.Zero
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Assert — only the initial fetch, no polling
        Assert.Equal(1, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that a disabled observer does not poll, even when
    /// <c>RefetchInterval</c> is set.
    /// </summary>
    [Fact]
    public async Task Disabled_Observer_Should_Not_Poll()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-disabled"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return "data";
                },
                Enabled = false,
                RefetchInterval = TimeSpan.FromMilliseconds(50)
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(200);

        // Assert — no fetches at all
        Assert.Equal(0, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="QueryObserver{TData,TQueryData}.SetOptions"/>
    /// starts and stops the polling timer when the interval changes.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Update_RefetchInterval()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);
        var secondFetchReached = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-setoptions"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetchDone.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchInterval = TimeSpan.Zero // Polling off initially
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Only the initial fetch should have occurred
        Assert.Equal(1, fetchCount);

        // Act — enable polling via SetOptions
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["poll-setoptions"],
            QueryFn = async _ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                if (count >= 2) secondFetchReached.Release();
                return $"data-v{count}";
            },
            Enabled = true,
            RefetchInterval = TimeSpan.FromMilliseconds(50)
        });

        await secondFetchReached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fetchCount >= 2, $"Expected at least 2 fetches after enabling polling, got {fetchCount}");

        // Act — disable polling via SetOptions
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["poll-setoptions"],
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return "data";
            },
            Enabled = true,
            RefetchInterval = TimeSpan.Zero // Polling off again
        });

        // Allow any already-queued timer callback to drain before snapshotting
        await Task.Delay(100);
        var countAfterDisable = fetchCount;
        await Task.Delay(200);

        // Assert — no additional fetches after the timer drain period
        Assert.Equal(countAfterDisable, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="QueryObserverOptions{TData,TQueryData}.RefetchIntervalFn"/>
    /// is called with the query and its returned interval is used for polling.
    /// Mirrors TanStack's <c>refetchInterval: (query) => number</c> form.
    /// </summary>
    [Fact]
    public async Task RefetchIntervalFn_Should_Use_Function_Return_Value_For_Polling()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var secondFetchReached = new SemaphoreSlim(0, 1);
        Query<string>? receivedQuery = null;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-fn-basic"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count >= 2) secondFetchReached.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchIntervalFn = query =>
                {
                    receivedQuery = query;
                    return TimeSpan.FromMilliseconds(50);
                }
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });

        // Wait for at least two fetches (initial + one poll tick)
        await secondFetchReached.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — function was called and polling occurred
        Assert.True(fetchCount >= 2, $"Expected at least 2 fetches, got {fetchCount}");
        Assert.NotNull(receivedQuery);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="QueryObserverOptions{TData,TQueryData}.RefetchIntervalFn"/>
    /// returning <see cref="TimeSpan.Zero"/> disables polling.
    /// Mirrors TanStack's <c>refetchInterval: () => false</c> form.
    /// </summary>
    [Fact]
    public async Task RefetchIntervalFn_Returning_Zero_Should_Disable_Polling()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-fn-null"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetchDone.Release();
                    return "data";
                },
                Enabled = true,
                RefetchIntervalFn = _ => TimeSpan.Zero // Disable polling
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Assert — only the initial fetch, no polling
        Assert.Equal(1, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="QueryObserverOptions{TData,TQueryData}.RefetchIntervalFn"/>
    /// takes precedence over the static <see cref="QueryObserverOptions{TData,TQueryData}.RefetchInterval"/>.
    /// </summary>
    [Fact]
    public async Task RefetchIntervalFn_Should_Take_Precedence_Over_Static_Interval()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-fn-precedence"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetchDone.Release();
                    return "data";
                },
                Enabled = true,
                // Static says poll every 50ms, but function says disabled
                RefetchInterval = TimeSpan.FromMilliseconds(50),
                RefetchIntervalFn = _ => TimeSpan.Zero
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Assert — function wins, no polling
        Assert.Equal(1, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that the refetch interval function can dynamically change the
    /// interval based on query state (e.g., disable polling after data reaches
    /// a certain condition). Re-evaluated after every state change, matching
    /// TanStack's <c>#updateTimers()</c> call in <c>onQueryUpdate</c>.
    /// </summary>
    [Fact]
    public async Task RefetchIntervalFn_Should_Stop_Polling_When_Function_Returns_Zero_After_State_Change()
    {
        // Arrange — poll until fetchCount reaches 3, then disable
        var client = CreateQueryClient();
        var fetchCount = 0;
        var targetReached = new SemaphoreSlim(0, 1);
        var fnCallCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-fn-stop"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count >= 3) targetReached.Release();
                    return $"data-v{count}";
                },
                Enabled = true,
                RefetchIntervalFn = query =>
                {
                    Interlocked.Increment(ref fnCallCount);

                    // Disable polling once we have data indicating 3+ fetches.
                    // The function sees query state, so after the 3rd fetch
                    // succeeds it returns TimeSpan.Zero to stop.
                    if (query.State is { Data: { } data } && data.EndsWith('3'))
                    {
                        return TimeSpan.Zero;
                    }

                    return TimeSpan.FromMilliseconds(50);
                }
            }
        );

        // Act
        var subscription = observer.Subscribe(_ => { });
        await targetReached.WaitAsync(TimeSpan.FromSeconds(5));

        // Let additional timer ticks pass — polling should have stopped
        await Task.Delay(300);

        // Assert — function was called, and polling stopped around fetch 3
        Assert.True(fetchCount >= 3, $"Expected at least 3 fetches, got {fetchCount}");

        // Allow some slack: a 4th fetch may have been in-flight when the
        // function returned null, but no more than that.
        Assert.True(fetchCount <= 5, $"Expected polling to stop around 3 fetches, got {fetchCount}");
        Assert.True(fnCallCount >= 3, $"Expected function to be called at least 3 times, got {fnCallCount}");

        subscription.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="QueryObserver{TData,TQueryData}.SetOptions"/>
    /// can switch from a static interval to a function form and vice versa.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Switch_Between_Static_And_Function_RefetchInterval()
    {
        // Arrange — start with static interval
        var client = CreateQueryClient();
        var fetchCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);
        var secondFetchReached = new SemaphoreSlim(0, 1);

        Func<QueryFunctionContext, Task<string>> queryFn = async _ =>
        {
            var count = Interlocked.Increment(ref fetchCount);
            if (count == 1) initialFetchDone.Release();
            if (count >= 2) secondFetchReached.Release();
            return $"data-v{count}";
        };

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["poll-switch"],
                QueryFn = queryFn,
                Enabled = true,
                RefetchInterval = TimeSpan.Zero // No polling initially
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Assert.Equal(1, fetchCount);

        // Act — switch to function form
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["poll-switch"],
            QueryFn = queryFn,
            Enabled = true,
            RefetchIntervalFn = _ => TimeSpan.FromMilliseconds(50)
        });

        await secondFetchReached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fetchCount >= 2, $"Expected at least 2 fetches after enabling function polling, got {fetchCount}");

        // Act — switch back to static (disabled)
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["poll-switch"],
            QueryFn = queryFn,
            Enabled = true,
            RefetchInterval = TimeSpan.Zero
        });

        // Allow any already-queued timer callback to drain before snapshotting
        await Task.Delay(100);
        var countAfterDisable = fetchCount;
        await Task.Delay(200);

        // Assert — no additional fetches after the timer drain period
        Assert.Equal(countAfterDisable, fetchCount);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should allow staleTime as a function"
    /// StaleTimeFn receives the query and returns a dynamic stale duration.
    /// Data should transition from fresh to stale at the derived time.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StaleTime_Function_Should_Derive_StaleTime_From_Query()
    {
        // Arrange — FakeTimeProvider for deterministic time control
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);

        var fetched = new TaskCompletionSource<bool>();
        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-fn-test"],
                QueryFn = _ =>
                {
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                // Dynamic staleTime: 20ms, derived from query state
                StaleTimeFn = query => query.State?.Data is not null
                    ? TimeSpan.FromMilliseconds(20)
                    : TimeSpan.Zero
            });

        var results = new List<IQueryResult<string>>();
        using var subscription = observer.Subscribe(r =>
        {
            if (r.Data is not null) results.Add(r);
        });

        // Act — wait for fetch to complete
        await fetched.Task;

        // Assert — data is fresh immediately after fetch
        Assert.NotEmpty(results);
        Assert.False(results[0].IsStale);

        // Advance time past the 20ms stale threshold
        timeProvider.Advance(TimeSpan.FromMilliseconds(21));

        // Re-read: IsStale is computed dynamically from the clock
        var currentResult = observer.GetCurrentResult();
        Assert.True(currentResult.IsStale);
    }

    /// <summary>
    /// TanStack: "should not see queries as stale is staleTime is Static"
    /// When staleTime is <see cref="Timeout.InfiniteTimeSpan"/> (C# equivalent of
    /// TanStack's <c>'static'</c>), data should never be considered stale — not even
    /// after invalidation. But no data = still stale (must fetch first).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StaleTime_Static_Should_Never_Be_Stale_After_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-stale"],
                QueryFn = _ =>
                {
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                StaleTime = Timeout.InfiniteTimeSpan
            });

        // Before data: result should be stale (no data yet)
        var beforeData = observer.GetCurrentResult();
        Assert.True(beforeData.IsStale);

        // Subscribe to trigger the fetch
        var results = new List<IQueryResult<string>>();
        using var subscription = observer.Subscribe(r =>
        {
            if (r.Data is not null) results.Add(r);
        });

        await fetched.Task;

        // After data: result should NOT be stale (static)
        Assert.NotEmpty(results);
        Assert.False(results[0].IsStale);
    }

    /// <summary>
    /// Static staleTime should keep data not-stale even after explicit
    /// invalidation. This is the core claim: "not even after invalidation."
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StaleTime_Static_Should_Not_Be_Stale_After_Invalidation()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-invalidate"],
                QueryFn = _ =>
                {
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                StaleTime = Timeout.InfiniteTimeSpan
            });

        using var subscription = observer.Subscribe(_ => { });
        await fetched.Task;

        // Sanity — data is not stale before invalidation
        Assert.False(observer.GetCurrentResult().IsStale);

        // Act — invalidate without refetching
        await client.InvalidateQueries(new InvalidateQueryFilters
        {
            QueryKey = ["static-invalidate"],
            RefetchType = InvalidateRefetchType.None
        });

        // Assert — still not stale despite invalidation (static staleTime)
        var result = observer.GetCurrentResult();
        Assert.False(result.IsStale);
    }

    /// <summary>
    /// Static staleTime should prevent refetch on window focus, even with
    /// <c>RefetchOnWindowFocus = Always</c>. Mirrors TanStack's
    /// "should not refetchOnWindowFocus when staleTime is static".
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StaleTime_Static_Should_Not_Refetch_On_Window_Focus()
    {
        // Arrange
        var focusManager = new FocusManager();
        var client = new QueryClient(new QueryCache(), focusManager: focusManager);
        var fetchCount = 0;
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-focus"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                StaleTime = Timeout.InfiniteTimeSpan,
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });

        using var subscription = observer.Subscribe(_ => { });
        await fetched.Task;

        // Act — simulate focus change
        Assert.Equal(1, fetchCount);
        Assert.False(observer.ShouldFetchOnWindowFocus());

        // The focus event path is synchronous: SetFocused → OnFocusChanged →
        // OnFocus → ShouldFetchOnWindowFocus (returns false) → no async work
        // started. No delay needed.
        focusManager.SetFocused(false);
        focusManager.SetFocused(true);

        // Assert — no refetch
        Assert.Equal(1, fetchCount);
    }

    /// <summary>
    /// Static staleTime via StaleTimeFn should also prevent refetch on focus.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StaleTimeFn_Returning_Static_Should_Not_Refetch_On_Focus()
    {
        // Arrange
        var focusManager = new FocusManager();
        var client = new QueryClient(new QueryCache(), focusManager: focusManager);
        var fetchCount = 0;
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-fn-focus"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                StaleTimeFn = _ => Timeout.InfiniteTimeSpan,
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });

        using var subscription = observer.Subscribe(_ => { });
        await fetched.Task;

        // Assert — static prevents refetch on focus
        Assert.Equal(1, fetchCount);
        Assert.False(observer.ShouldFetchOnWindowFocus());
    }

    /// <summary>
    /// RefetchQueries should skip queries with static staleTime, matching
    /// TanStack's <c>.filter(q => !q.isDisabled() && !q.isStatic())</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchQueries_Should_Skip_Static_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var staticFetchCount = 0;
        var normalFetchCount = 0;
        var staticFetched = new TaskCompletionSource<bool>();
        var normalFetched = new TaskCompletionSource<bool>();

        var staticObserver = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-static", "a"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref staticFetchCount);
                    staticFetched.TrySetResult(true);
                    return Task.FromResult("static-data");
                },
                StaleTime = Timeout.InfiniteTimeSpan
            });

        var normalObserver = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-static", "b"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref normalFetchCount);
                    normalFetched.TrySetResult(true);
                    return Task.FromResult("normal-data");
                }
            });

        using var sub1 = staticObserver.Subscribe(_ => { });
        using var sub2 = normalObserver.Subscribe(_ => { });
        await Task.WhenAll(staticFetched.Task, normalFetched.Task);

        // Both queries fetched once during initial mount
        Assert.Equal(1, staticFetchCount);
        Assert.Equal(1, normalFetchCount);

        // Act — refetch all queries under the ["refetch-static"] prefix
        await client.RefetchQueries(["refetch-static"]);

        // Assert — static query was skipped, normal query was refetched
        Assert.Equal(1, staticFetchCount);
        Assert.Equal(2, normalFetchCount);
    }

    /// <summary>
    /// Static staleTime in FetchQueryOptions should read from cache even
    /// after invalidation. Mirrors TanStack's
    /// "should read from cache with static staleTime even if invalidated".
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task FetchQuery_Static_StaleTime_Should_Read_Cache_Even_If_Invalidated()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var first = await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["fetch-static"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult("data");
            },
            StaleTime = Timeout.InfiniteTimeSpan
        });

        Assert.Equal(1, fetchCount);
        Assert.Equal("data", first);

        // Invalidate without refetching
        await client.InvalidateQueries(new InvalidateQueryFilters
        {
            QueryKey = ["fetch-static"],
            RefetchType = InvalidateRefetchType.None
        });

        // Act — fetch again with static staleTime
        var second = await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["fetch-static"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult("new-data");
            },
            StaleTime = Timeout.InfiniteTimeSpan
        });

        // Assert — returned cached data, did not refetch
        Assert.Equal(1, fetchCount);
        Assert.Equal("data", second);
    }

    /// <summary>
    /// TanStack: "should throw an error if throwOnError option is true"
    /// When <c>RefetchOptions.ThrowOnError = true</c>, errors propagate to the caller.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task ThrowOnError_Should_Throw_On_Refetch_When_True()
    {
        // Arrange — set Retry=0 globally so the query fails immediately
        var client = CreateQueryClient();
        client.SetDefaultOptions(new QueryClientDefaultOptions { Retry = 0 });

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["throw-test"],
                QueryFn = _ => throw new InvalidOperationException("fetch error"),
                Enabled = false
            });

        // Act & Assert
        var act = () => observer.RefetchAsync(new RefetchOptions { ThrowOnError = true });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("fetch error", ex.Message);
    }

    /// <summary>
    /// Default behavior: errors are suppressed and captured in the result state.
    /// Matches TanStack's <c>#executeFetch</c> which does <c>promise.catch(noop)</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task ThrowOnError_Default_Should_Suppress_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetDefaultOptions(new QueryClientDefaultOptions { Retry = 0 });

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["suppress-default"],
                QueryFn = _ => throw new InvalidOperationException("suppressed"),
                Enabled = false
            });

        // Act — no RefetchOptions passed, errors suppressed by default
        var result = await observer.RefetchAsync();

        // Assert — error captured in result, not thrown
        Assert.True(result.IsError);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("suppressed", result.Error!.Message);
    }

    /// <summary>
    /// Explicit <c>ThrowOnError = false</c> also suppresses errors.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task ThrowOnError_False_Should_Suppress_Error_And_Set_Result()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetDefaultOptions(new QueryClientDefaultOptions { Retry = 0 });

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["suppress-explicit"],
                QueryFn = _ => throw new InvalidOperationException("suppressed too"),
                Enabled = false
            });

        // Act
        var result = await observer.RefetchAsync(new RefetchOptions { ThrowOnError = false });

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("suppressed too", result.Error!.Message);
    }

    /// <summary>
    /// <see cref="OperationCanceledException"/> is always propagated regardless
    /// of <c>ThrowOnError</c> — cancellation is not a query error, it's a signal.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchAsync_Should_Propagate_OperationCanceledException()
    {
        // Arrange
        var client = CreateQueryClient();
        var cts = new CancellationTokenSource();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["cancel-propagate"],
                QueryFn = async ctx =>
                {
                    await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
                    return "unreachable";
                },
                Enabled = false
            });

        // Act — cancel while fetching, with default ThrowOnError = false
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var act = () => observer.RefetchAsync(cancellationToken: cts.Token);

        // Assert — OperationCanceledException (or its subclass TaskCanceledException)
        // always propagates
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    /// <summary>
    /// TanStack: "should refetchOnWindowFocus when query has background error and
    /// staleTime is not static". After a successful initial fetch, a background
    /// refetch that errors should set <c>IsInvalidated = true</c>, making the query
    /// stale despite a non-zero <c>StaleTime</c>. <c>ShouldFetchOnWindowFocus</c>
    /// should return <c>true</c> so the next focus event triggers a refetch.
    ///
    /// Tests the predicate directly rather than the focus event to avoid cross-test
    /// interference through the singleton <see cref="FocusManager"/>.
    /// </summary>
    [Fact]
    public async Task ShouldFetchOnWindowFocus_After_Background_Error_With_StaleTime()
    {
        // Arrange
        var client = CreateQueryClient();
        // Not testing retry — avoid 7s backoff wait
        client.SetQueryDefaults(["focus-bg-error"], new QueryDefaults { QueryKey = ["focus-bg-error"], Retry = 0 });
        var callCount = 0;
        var initialFetchDone = new SemaphoreSlim(0, 1);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-bg-error"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref callCount);
                    if (count == 1)
                    {
                        initialFetchDone.Release();
                        return "data";
                    }
                    throw new InvalidOperationException("background error");
                },
                StaleTime = TimeSpan.FromSeconds(60), // data considered fresh for 60s
                RefetchOnWindowFocus = RefetchOnBehavior.WhenStale,
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });

        try
        {
            // Wait for initial fetch to succeed
            await initialFetchDone.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(10); // let state propagate
            Assert.Equal("data", observer.GetCurrentResult().Data);

            // Before background error, data is fresh → should NOT refetch on focus
            Assert.False(observer.ShouldFetchOnWindowFocus());

            // Trigger a background refetch that fails. Errors are suppressed
            // by default (ThrowOnError = false), so no try/catch needed.
            await observer.RefetchAsync();

            // After error, query is invalidated → should refetch on focus even
            // though the 60s staleTime window hasn't elapsed
            Assert.True(observer.ShouldFetchOnWindowFocus());
        }
        finally
        {
            subscription.Dispose();
        }
    }

    /// <summary>
    /// TanStack: "shouldFetchOnWindowFocus should respect refetchOnWindowFocus option"
    /// Verifies the tri-state per-observer refetchOnWindowFocus behavior.
    /// </summary>
    [Fact]
    public void RefetchOnWindowFocus_Should_Be_Configurable_Per_Observer()
    {
        // Arrange
        var client = CreateQueryClient();

        // Observer with Never — should not fetch on focus
        var neverObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-never"],
                QueryFn = async _ => "data",
                RefetchOnWindowFocus = RefetchOnBehavior.Never
            });

        // Observer with WhenStale (default, StaleTime=0 means always stale)
        var staleObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-stale"],
                QueryFn = async _ => "data"
            });

        // Observer with Always — should always fetch on focus
        var alwaysObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-always"],
                QueryFn = async _ => "data",
                RefetchOnWindowFocus = RefetchOnBehavior.Always,
                StaleTime = TimeSpan.MaxValue // data is fresh, but Always overrides
            });

        // Assert
        Assert.False(neverObserver.ShouldFetchOnWindowFocus());
        Assert.True(staleObserver.ShouldFetchOnWindowFocus());
        Assert.True(alwaysObserver.ShouldFetchOnWindowFocus());
    }

    /// <summary>
    /// Null NotifyOnChangeProps (default) notifies on every state change —
    /// backward compatible baseline.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_Null_Notifies_On_Every_Change()
    {
        // Arrange
        var client = CreateQueryClient();
        var notifyCount = 0;
        var fetchComplete = new TaskCompletionSource();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-null"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    return "data";
                },
                // NotifyOnChangeProps defaults to null
            });

        // Act
        var sub = observer.Subscribe(result =>
        {
            notifyCount++;
            if (result.IsSuccess)
                fetchComplete.TrySetResult();
        });

        await fetchComplete.Task;

        // Assert — at least the FetchStatus and Success transitions fire
        Assert.True(notifyCount >= 2);

        sub.Dispose();
    }

    /// <summary>
    /// With NotifyOnChangeProps = { Data }, listener is called when Data changes
    /// but NOT during FetchStatus-only transitions.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_Data_Only_Skips_FetchStatus_Transitions()
    {
        // Arrange
        var client = CreateQueryClient();
        var notifications = new List<IQueryResult<string>>();
        var fetchComplete = new TaskCompletionSource();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-data-only"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    return "result";
                },
                NotifyOnChangeProps = new HashSet<string> { QueryResultProps.Data },
            });

        // Act
        var sub = observer.Subscribe(result =>
        {
            notifications.Add(result);
            if (result.Data is not null)
                fetchComplete.TrySetResult();
        });

        await fetchComplete.Task;

        // Assert — the first notification always fires (prevResult is null),
        // then only notifications where Data actually changed should follow.
        // FetchStatus-only transitions (Idle→Fetching) are skipped.
        Assert.All(notifications.Skip(1), n =>
            Assert.Equal("result", n.Data));

        sub.Dispose();
    }

    /// <summary>
    /// Empty NotifyOnChangeProps set suppresses all notifications — the listener
    /// is never called even though the query state changes.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_Empty_Set_Never_Notifies()
    {
        // Arrange
        var client = CreateQueryClient();
        var notifyCount = 0;

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-empty"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    return "data";
                },
                NotifyOnChangeProps = new HashSet<string>(),
            });

        // Act
        var sub = observer.Subscribe(_ => notifyCount++);

        // Allow the fetch cycle to complete
        await Task.Delay(100);

        // Assert — no tracked properties means no notifications fire, even
        // though the query transitions through Pending→Fetching→Succeeded.
        Assert.Equal(0, notifyCount);

        sub.Dispose();
    }

    /// <summary>
    /// NotifyOnChangeProps = { Data, Error } fires for both data success and error transitions.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_Multiple_Props_Fires_For_Data_And_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var notifications = new List<IQueryResult<string>>();
        var shouldFail = true;
        var errorSeen = new TaskCompletionSource();
        var successSeen = new TaskCompletionSource();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-multi"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    if (shouldFail)
                        throw new InvalidOperationException("fail");
                    return "ok";
                },
                NotifyOnChangeProps = new HashSet<string>
                {
                    QueryResultProps.Data,
                    QueryResultProps.Error,
                },
            });

        // Subscribe and set retry to 0 so the error surfaces immediately
        client.GetQueryCache().Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["notify-multi"],
            QueryKeyHasher = DefaultQueryKeyHasher.Instance,
            Retry = 0,
        });

        var sub = observer.Subscribe(result =>
        {
            notifications.Add(result);
            if (result.IsError) errorSeen.TrySetResult();
            if (result.IsSuccess) successSeen.TrySetResult();
        });

        await errorSeen.Task;

        // Now succeed on next fetch
        shouldFail = false;
        _ = observer.RefetchAsync();
        await successSeen.Task;

        // Assert — notifications include error and success transitions
        Assert.Contains(notifications, n => n.Error is not null);
        Assert.Contains(notifications, n => n.Data == "ok");

        sub.Dispose();
    }

    /// <summary>
    /// First notification always fires regardless of NotifyOnChangeProps setting.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_First_Notification_Always_Fires()
    {
        // Arrange
        var client = CreateQueryClient();
        var firstNotification = new TaskCompletionSource<IQueryResult<string>>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-first"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    return "data";
                },
                // Status would filter out everything, but first still fires
                NotifyOnChangeProps = new HashSet<string> { QueryResultProps.Status },
            });

        // Act
        var sub = observer.Subscribe(result =>
            firstNotification.TrySetResult(result));

        var result = await firstNotification.Task;

        // Assert — received the first notification even though Status may not
        // have changed from its initial value
        Assert.NotNull(result);

        sub.Dispose();
    }

    /// <summary>
    /// With NotifyOnChangeProps = { Data }, a refetch returning the same data
    /// does not trigger a notification beyond the initial one.
    /// </summary>
    [Fact]
    public async Task NotifyOnChangeProps_Refetch_Same_Data_Skips_Notification()
    {
        // Arrange
        var client = CreateQueryClient();
        var notifyCount = 0;
        var firstFetch = new TaskCompletionSource();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["notify-same-data"],
                QueryFn = async _ =>
                {
                    await Task.Yield();
                    return "stable";
                },
                NotifyOnChangeProps = new HashSet<string> { QueryResultProps.Data },
            });

        var sub = observer.Subscribe(result =>
        {
            notifyCount++;
            if (result.IsSuccess) firstFetch.TrySetResult();
        });

        await firstFetch.Task;
        var countAfterFirstFetch = notifyCount;

        // Act — refetch returns the same data
        await observer.RefetchAsync();

        // Assert — no additional Data-change notification since data is unchanged
        Assert.Equal(countAfterFirstFetch, notifyCount);

        sub.Dispose();
    }

    #endregion

    #region QueryUtilities

    [Fact]
    public void KeepPreviousData_Should_Return_Previous_Data()
    {
        // Arrange
        var previousData = "previous";

        // Act
        var result = QueryUtilities.KeepPreviousData<string>(previousData, null);

        // Assert
        Assert.Equal("previous", result);
    }

    [Fact]
    public void KeepPreviousData_Should_Return_Null_When_No_Previous_Data()
    {
        // Act
        var result = QueryUtilities.KeepPreviousData<string>(null, null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Of_Should_Return_Stable_Delegate_With_Static_Value()
    {
        // Arrange
        var placeholder = QueryUtilities.Of("static-value");

        // Act
        var result1 = placeholder(null, null);
        var result2 = placeholder("other", null);

        // Assert — always returns the same static value regardless of input
        Assert.Equal("static-value", result1);
        Assert.Equal("static-value", result2);
    }

    #endregion

    #region RefetchOnMount

    /// <summary>
    /// Initial load should fetch even when RefetchOnMount is Never — RefetchOnMount
    /// only controls *re*fetching when data already exists.
    /// TanStack useQuery.test.tsx:591.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchOnMount_Never_Should_Still_Fetch_On_Initial_Load()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var fetched = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-never-initial"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    fetched.TrySetResult(true);
                    return Task.FromResult("data");
                },
                RefetchOnMount = RefetchOnBehavior.Never
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });
        await fetched.Task;

        // Assert — initial load always fetches
        Assert.Equal(1, fetchCount);
    }

    /// <summary>
    /// When data already exists and RefetchOnMount is Never, subscribing should not
    /// trigger a refetch. TanStack useQuery.test.tsx:613.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void RefetchOnMount_Never_Should_Not_Refetch_When_Data_Exists()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        client.SetQueryData(["mount-never-existing"], "prefetched");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-never-existing"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult("fresh");
                },
                RefetchOnMount = RefetchOnBehavior.Never
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });

        // Assert — no refetch, data is already available
        Assert.Equal(0, fetchCount);
        Assert.Equal("prefetched", observer.GetCurrentResult().Data);
    }

    /// <summary>
    /// RefetchOnMount=Always should refetch even when data is fresh (large finite StaleTime).
    /// TanStack useQuery.test.tsx:2731.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchOnMount_Always_Should_Refetch_Fresh_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var secondFetch = new TaskCompletionSource<bool>();

        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["mount-always-fresh"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult("prefetched");
            }
        });

        Assert.Equal(1, fetchCount);

        // Act — create observer with Always + large StaleTime (data is still fresh)
        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-always-fresh"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    secondFetch.TrySetResult(true);
                    return Task.FromResult("refreshed");
                },
                RefetchOnMount = RefetchOnBehavior.Always,
                StaleTime = TimeSpan.FromHours(1)
            });

        using var subscription = observer.Subscribe(_ => { });
        await secondFetch.Task;

        // Assert — Always overrides freshness
        Assert.Equal(2, fetchCount);
    }

    /// <summary>
    /// Default WhenStale should refetch when data is stale (StaleTime=0).
    /// TanStack useQuery.test.tsx:2768.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchOnMount_WhenStale_Default_Should_Refetch_Stale_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var secondFetch = new TaskCompletionSource<bool>();

        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["mount-stale-default"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult("prefetched");
            }
        });

        Assert.Equal(1, fetchCount);

        // Act — default StaleTime=0 means data is immediately stale
        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-stale-default"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    secondFetch.TrySetResult(true);
                    return Task.FromResult("refreshed");
                }
            });

        using var subscription = observer.Subscribe(_ => { });
        await secondFetch.Task;

        // Assert — stale data triggers refetch
        Assert.Equal(2, fetchCount);
    }

    /// <summary>
    /// RefetchOnMount=Always should refetch even with a positive StaleTime that
    /// hasn't elapsed yet. TanStack useQuery.test.tsx:3089.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RefetchOnMount_Always_Should_Refetch_With_Positive_StaleTime()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var secondFetch = new TaskCompletionSource<bool>();

        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["mount-always-staletime"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult("prefetched");
            }
        });

        Assert.Equal(1, fetchCount);

        // Act — data is fresh (50ms not elapsed), but Always forces refetch
        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-always-staletime"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    secondFetch.TrySetResult(true);
                    return Task.FromResult("refreshed");
                },
                RefetchOnMount = RefetchOnBehavior.Always,
                StaleTime = TimeSpan.FromMilliseconds(50)
            });

        using var subscription = observer.Subscribe(_ => { });
        await secondFetch.Task;

        // Assert — Always overrides the non-expired StaleTime
        Assert.Equal(2, fetchCount);
    }

    /// <summary>
    /// Static StaleTime (InfiniteTimeSpan) should block refetch even with
    /// RefetchOnMount=Always. Static means "never refetch under any trigger."
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void RefetchOnMount_Always_Should_Not_Refetch_With_Static_StaleTime()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        client.SetQueryData(["mount-always-static"], "prefetched");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mount-always-static"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult("refreshed");
                },
                RefetchOnMount = RefetchOnBehavior.Always,
                StaleTime = Timeout.InfiniteTimeSpan
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });

        // Assert — static blocks even Always
        Assert.Equal(0, fetchCount);
        Assert.Equal("prefetched", observer.GetCurrentResult().Data);
    }

    #endregion

    #region Stale Timeout and GC Race

    /// <summary>
    /// When GC time is short and the observer unsubscribes, the GC timer starts.
    /// AddObserver (called from OnSubscribe) must cancel the GC timer so a
    /// re-subscribe finds cached data instead of a fresh, empty query.
    /// Validates the ClearGcTimeout() call in Query.AddObserver (query.ts:348).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Resubscribe_After_GcTime_Should_See_Cached_Data()
    {
        // Arrange — short GC time so the timer fires during the test
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);
        var fetchCount = 0;

        // Pre-populate the cache so the observer starts with data.
        client.SetQueryData(["gc-race"], "cached");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-race"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult("refetched");
                },
                StaleTime = TimeSpan.FromMinutes(10),
                CacheTime = TimeSpan.FromSeconds(5)
            });

        // Subscribe — observer attaches, sees cached data, no fetch (fresh).
        var sub1 = observer.Subscribe(_ => { });
        Assert.Equal(0, fetchCount);
        Assert.Equal("cached", observer.GetCurrentResult().Data);

        // Unsubscribe — starts the 5s GC timer.
        sub1.Dispose();

        // Advance past the GC time. Without ClearGcTimeout in AddObserver,
        // the query would be removed from the cache.
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Re-subscribe — AddObserver should have cancelled the GC timer during
        // the first subscribe, but since we unsubscribed, the GC timer ran.
        // The key insight: a *new* observer with the same key should still
        // find the data in cache because AddObserver cancels GC on re-attach.
        var observer2 = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-race"],
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult("refetched");
                },
                StaleTime = TimeSpan.FromMinutes(10),
                CacheTime = TimeSpan.FromSeconds(5)
            });

        using var sub2 = observer2.Subscribe(_ => { });

        // Assert — data is still fresh from cache, no new fetch
        Assert.Equal("cached", observer2.GetCurrentResult().Data);
        Assert.False(observer2.GetCurrentResult().IsStale);
    }

    /// <summary>
    /// UpdateStaleTimeout should schedule a timer that notifies listeners when
    /// data transitions from fresh to stale. Validates queryObserver.ts:354-376.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StaleTimeout_Should_Notify_When_Data_Becomes_Stale()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);

        client.SetQueryData(["stale-timer"], "data");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-timer"],
                QueryFn = _ => Task.FromResult("data"),
                StaleTime = TimeSpan.FromSeconds(30)
            });

        var results = new List<IQueryResult<string>>();
        using var subscription = observer.Subscribe(r => results.Add(r));

        // Immediately after subscribe, data should be fresh.
        Assert.False(observer.GetCurrentResult().IsStale);

        // Advance just under the stale threshold — still fresh.
        timeProvider.Advance(TimeSpan.FromSeconds(29));
        Assert.False(observer.GetCurrentResult().IsStale);

        // Advance past the stale threshold (+ the 1ms buffer).
        // The stale timeout timer should fire and push a new result.
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        // The latest result from the listener should now show stale.
        Assert.NotEmpty(results);
        Assert.True(results[^1].IsStale);
    }

    /// <summary>
    /// StaleTime = TimeSpan.MaxValue should not throw ArgumentOutOfRangeException
    /// in UpdateStaleTimeout. The +1ms TanStack adjustment pushes the timer past
    /// what TimeSpan.FromMilliseconds can represent — the overflow guards must
    /// skip scheduling instead of throwing. In TanStack, Infinity is filtered by
    /// isValidTimeout (utils.ts:104-106); in C# the overflow guards serve the
    /// same purpose for TimeSpan.MaxValue.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StaleTimeout_Should_Not_Throw_With_MaxValue_StaleTime()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);

        client.SetQueryData(["stale-overflow"], "data");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-overflow"],
                QueryFn = _ => Task.FromResult("data"),
                StaleTime = TimeSpan.MaxValue
            });

        // Act — Subscribe triggers UpdateStaleTimeout internally.
        // Before the overflow fix, this threw ArgumentOutOfRangeException.
        var exception = Record.Exception(() => observer.Subscribe(_ => { }));

        // Assert — no exception and data stays fresh (no stale timer scheduled).
        Assert.Null(exception);
        Assert.False(observer.GetCurrentResult().IsStale);

        // Advancing time should not cause a stale notification — there is no timer.
        timeProvider.Advance(TimeSpan.FromHours(1));
        Assert.False(observer.GetCurrentResult().IsStale);
    }

    /// <summary>
    /// Unsubscribing should clear the stale timeout timer so no notification
    /// fires after the observer is detached. Validates the ClearStaleTimeout()
    /// call in Destroy() (queryObserver.ts:131-136).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StaleTimeout_Should_Not_Fire_After_Unsubscribe()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);

        client.SetQueryData(["stale-cleanup"], "data");

        var observer = new QueryObserver<string, string>(client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-cleanup"],
                QueryFn = _ => Task.FromResult("data"),
                StaleTime = TimeSpan.FromSeconds(30)
            });

        var callbackCount = 0;
        var subscription = observer.Subscribe(_ => callbackCount++);

        // Initial subscribe fires a callback for the current result.
        var initialCount = callbackCount;

        // Unsubscribe — should clear the stale timeout.
        subscription.Dispose();

        // Advance past stale threshold — timer should NOT fire.
        timeProvider.Advance(TimeSpan.FromSeconds(60));

        // Assert — no new callbacks after unsubscribe.
        Assert.Equal(initialCount, callbackCount);
    }

    #endregion

    #region SetOptions_and_CacheNotifications

    /// <summary>
    /// TanStack ref: queryObserver.test.tsx:1130-1148
    /// Calling SetOptions on a subscribed observer should fire a
    /// <see cref="QueryCacheObserverOptionsUpdatedEvent"/> through the QueryCache's
    /// event stream.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SetOptions_NotifiesCacheListeners()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var receivedEvents = new List<QueryCacheObserverOptionsUpdatedEvent>();

        var cacheSubscription = cache.Subscribe(evt =>
        {
            if (evt is QueryCacheObserverOptionsUpdatedEvent optionsUpdated)
                receivedEvents.Add(optionsUpdated);
        });

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["cache-notify-options"],
                QueryFn = async _ => "data",
                Enabled = false
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Act — update options on the subscribed observer
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["cache-notify-options"],
            QueryFn = async _ => "data-v2",
            Enabled = false
        });

        // Assert — a QueryCacheObserverOptionsUpdatedEvent should have been emitted
        Assert.Single(receivedEvents);
        Assert.NotNull(receivedEvents[0].Query);
        Assert.NotNull(receivedEvents[0].Observer);

        subscription.Dispose();
        cacheSubscription.Dispose();
    }

    /// <summary>
    /// TanStack ref: queryObserver.test.tsx — key change via SetOptions
    /// When SetOptions changes the query key, the observer should detach from
    /// the old query and attach to the new one.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SetOptions_KeyChange_SwitchesQuery()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["old-key"],
                QueryFn = async _ => "old-data",
                Enabled = false
            }
        );

        // Subscribing registers the observer with the "old-key" query
        var subscription = observer.Subscribe(_ => { });

        var oldHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["old-key"]);
        var oldQuery = cache.GetByHash(oldHash);
        Assert.NotNull(oldQuery);
        Assert.Equal(1, oldQuery!.ObserverCount);

        // Act — switch to a different key
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["new-key"],
            QueryFn = async _ => "new-data",
            Enabled = false
        });

        // Assert — old query lost its observer, new query gained one
        var newHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["new-key"]);
        var newQuery = cache.GetByHash(newHash);

        Assert.Equal(0, oldQuery.ObserverCount);
        Assert.NotNull(newQuery);
        Assert.Equal(1, newQuery!.ObserverCount);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack ref: queryObserver.test.tsx — enabled false to true
    /// Flipping Enabled from false to true via SetOptions should trigger a fetch
    /// for the already-subscribed observer.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task SetOptions_EnabledFalseToTrue_TriggersRefetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var fetchDone = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["enabled-toggle"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) fetchDone.TrySetResult(true);
                    return "data";
                },
                Enabled = false
            }
        );

        // Subscribe while disabled -- should NOT fetch
        var subscription = observer.Subscribe(_ => { });
        Assert.Equal(0, fetchCount);

        // Act — enable the observer via SetOptions
        observer.SetOptions(new QueryObserverOptions<string, string>
        {
            QueryKey = ["enabled-toggle"],
            QueryFn = async _ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                if (count == 1) fetchDone.TrySetResult(true);
                return "data";
            },
            Enabled = true
        });

        await fetchDone.Task;

        // Assert — a fetch was triggered after enabling
        Assert.True(fetchCount >= 1);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack ref: queryObserver.test.tsx:1150-1160
    /// A disabled observer with no data should report IsStale = false.
    /// Disabled observers should not trigger refetches, so marking them as
    /// stale would be misleading.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void DisabledObserver_IsNotStale()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-not-stale"],
                QueryFn = async _ => "data",
                Enabled = false
            }
        );

        // Act — subscribe and read the result
        var subscription = observer.Subscribe(_ => { });
        var result = observer.GetCurrentResult();

        // Assert — disabled observer with no data is not stale
        Assert.False(result.IsStale);
        Assert.True(result.IsPending);

        subscription.Dispose();
    }

    /// <summary>
    /// General invariant: after a successful fetch, GetCurrentResult() should
    /// return a consistent snapshot where Status, Data, and FetchStatus all
    /// agree on the outcome.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task GetCurrentResult_ConsistentSnapshot()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchDone = new TaskCompletionSource<bool>();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["consistent-snapshot"],
                QueryFn = _ =>
                {
                    fetchDone.TrySetResult(true);
                    return Task.FromResult("snapshot-data");
                },
                Enabled = true
            }
        );

        // Act — subscribe to trigger fetch, then wait for completion
        var subscription = observer.Subscribe(_ => { });
        await fetchDone.Task;

        // Small delay for the result dispatch to propagate
        await Task.Delay(50);

        var result = observer.GetCurrentResult();

        // Assert — Status == Succeeded implies Data is present and FetchStatus == Idle
        Assert.Equal(QueryStatus.Succeeded, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal("snapshot-data", result.Data);
        Assert.Equal(FetchStatus.Idle, result.FetchStatus);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFetching);
        Assert.False(result.IsPending);

        subscription.Dispose();
    }

    #endregion
}
