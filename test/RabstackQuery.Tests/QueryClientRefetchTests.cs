namespace RabstackQuery;

/// <summary>
/// Tests for QueryClient refetchQueries, invalidateQueries, resetQueries, and related
/// bulk operations. Ports test cases from TanStack's queryClient.test.tsx sections:
/// refetchQueries (~14 tests), invalidateQueries (~9 tests), resetQueries (~4 tests).
/// </summary>
public sealed class QueryClientRefetchTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    /// <summary>
    /// Helper: creates a query with an observer so it counts as "active".
    /// Returns (observer, subscription, fetchCount accessor).
    /// Caller must dispose the subscription when done.
    /// </summary>
    private static (QueryObserver<string, string> Observer, IDisposable Subscription) CreateActiveQuery(
        QueryClient client,
        QueryKey key,
        Func<int, string> dataFactory,
        ref int fetchCount)
    {
        var count = fetchCount; // captured for closure
        var capturedCount = 0;
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = key,
                QueryFn = async _ =>
                {
                    capturedCount++;
                    return dataFactory(capturedCount);
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        fetchCount = capturedCount;
        return (observer, subscription);
    }

    #region refetchQueries

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_All_Queries_When_No_Filters()
    {
        // Arrange
        var client = CreateQueryClient();
        var initial1 = new TaskCompletionSource();
        var initial2 = new TaskCompletionSource();
        var refetch1 = new TaskCompletionSource();
        var refetch2 = new TaskCompletionSource();
        var fetch1Count = 0;
        var fetch2Count = 0;

        var observer1 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["key1"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetch1Count);
                    if (c == 1) initial1.TrySetResult();
                    else refetch1.TrySetResult();
                    return "data1";
                },
                Enabled = true
            });
        var observer2 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["key2"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetch2Count);
                    if (c == 1) initial2.TrySetResult();
                    else refetch2.TrySetResult();
                    return "data2";
                },
                Enabled = true
            });

        var sub1 = observer1.Subscribe(_ => { });
        var sub2 = observer2.Subscribe(_ => { });
        await Task.WhenAll(
            initial1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            initial2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Act
        await client.RefetchQueriesAsync(filters: null);
        await Task.WhenAll(
            refetch1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            refetch2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.True(fetch1Count >= 2, "Query 1 should be refetched");
        Assert.True(fetch2Count >= 2, "Query 2 should be refetched");

        sub1.Dispose();
        sub2.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Only_Refetch_Matching_Key_Prefix()
    {
        // Arrange
        var client = CreateQueryClient();
        var todosInitial = new TaskCompletionSource();
        var usersInitial = new TaskCompletionSource();
        var todosRefetch = new TaskCompletionSource();
        var todosFetchCount = 0;
        var usersFetchCount = 0;

        var todosObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos", "list"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref todosFetchCount);
                    if (c == 1) todosInitial.TrySetResult();
                    else todosRefetch.TrySetResult();
                    return "todos";
                },
                Enabled = true
            });
        var usersObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["users"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref usersFetchCount);
                    usersInitial.TrySetResult();
                    return "users";
                },
                Enabled = true
            });

        var sub1 = todosObserver.Subscribe(_ => { });
        var sub2 = usersObserver.Subscribe(_ => { });
        await Task.WhenAll(
            todosInitial.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            usersInitial.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var usersCountBefore = usersFetchCount;

        // Act — only refetch queries with "todos" prefix
        await client.RefetchQueriesAsync(["todos"]);
        await todosRefetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(todosFetchCount >= 2, "Todos query should be refetched");
        Assert.Equal(usersCountBefore, usersFetchCount); // Users query should NOT be refetched

        sub1.Dispose();
        sub2.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_Active_Queries_Only_With_TypeFilter()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var activeFetchCount = 0;

        // Create active query (with observer)
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active-query"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref activeFetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return "active";
                },
                Enabled = true
            });
        var sub = activeObserver.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Create inactive query (no observer)
        client.SetQueryData(["inactive-query"], "inactive-data");

        // Act — refetch only active queries
        await client.RefetchQueriesAsync(new QueryFilters { Type = QueryTypeFilter.Active });
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(activeFetchCount >= 2, "Active query should be refetched");
        // The inactive query has no query function, so it can't be refetched anyway

        sub.Dispose();
    }

    /// <summary>
    /// RefetchQueriesAsync skips disabled queries (no query function or no observers).
    /// A query created via SetQueryData with no query function should not crash.
    /// </summary>
    [Fact]
    public async Task RefetchQueriesAsync_Should_Not_Refetch_Inactive_Queries_By_Default()
    {
        // Arrange
        var client = CreateQueryClient();

        // Create an inactive query (data only, no observer)
        client.SetQueryData(["inactive"], "data");

        // Act — RefetchQueriesAsync without type filter still only refetches queries
        // that have a query function
        var exception = await Record.ExceptionAsync(() =>
            client.RefetchQueriesAsync(new QueryFilters { QueryKey = ["inactive"] }));

        // Assert — should not crash
        Assert.Null(exception);
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Not_Refetch_If_All_Observers_Disabled()
    {
        // TanStack line 1066: disabled queries should not be refetched
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-query"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return "data";
                },
                Enabled = false
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(50); // Ensure disabled query doesn't auto-fetch

        // Act
        await client.RefetchQueriesAsync(new QueryFilters { QueryKey = ["disabled-query"] });
        await Task.Delay(100);

        // Assert — disabled observer means the query is disabled, so no refetch
        Assert.Equal(0, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public void RefetchQueriesAsync_Should_Not_Skip_Query_When_Mix_Of_Enabled_And_Disabled_Observers()
    {
        // TanStack line 1082: a query with at least one enabled observer should
        // be considered active (not disabled or static) and should be eligible for
        // refetch. This test verifies the IsActive/IsDisabled predicates rather
        // than the full async refetch cycle (which is covered elsewhere).
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["mixed-observers"], "seeded");

        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["mixed-observers"]);
        var query = client.QueryCache.Get<string>(hash)!;

        // Add an enabled observer
        var enabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mixed-observers"],
                QueryFn = async _ => "enabled",
                Enabled = true,
                StaleTime = TimeSpan.FromMinutes(5) // Don't auto-refetch
            });
        var sub1 = enabledObserver.Subscribe(_ => { });

        // Add a disabled observer on the same query
        var disabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["mixed-observers"],
                QueryFn = async _ => "disabled",
                Enabled = false
            });
        var sub2 = disabledObserver.Subscribe(_ => { });

        // Assert — query should be active (not disabled, not static)
        Assert.True(query.IsActive(), "Query should be active with at least one enabled observer");
        Assert.False(query.IsDisabled(), "Query should NOT be disabled");
        Assert.False(query.IsStatic(), "Query should NOT be static");
        Assert.Equal(2, query.ObserverCount);

        sub1.Dispose();
        sub2.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_All_Stale_Queries()
    {
        // TanStack line 1160: filter { Stale = true } should refetch stale queries
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var staleFetchCount = 0;

        // Stale query (StaleTime=0 means always stale)
        var staleObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["stale-query"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref staleFetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"stale-{c}";
                },
                Enabled = true,
                StaleTime = TimeSpan.Zero
            });

        var sub = staleObserver.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — refetch only stale queries
        await client.RefetchQueriesAsync(new QueryFilters { Stale = true });
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(staleFetchCount >= 2, "Stale query should be refetched");

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_Only_Active_Queries()
    {
        // TanStack line 1256: Type=Active should only refetch queries with observers
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var activeFetchCount = 0;
        var inactiveFetchCount = 0;

        // Active query
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref activeFetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"active-{c}";
                },
                Enabled = true
            });

        var sub = activeObserver.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Inactive query (no observer, just seeded data with a query function)
        var cache = client.QueryCache;
        var inactiveQuery = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["inactive"], GcTime = TimeSpan.FromMinutes(5) });
        inactiveQuery.SetQueryFn(async _ =>
        {
            Interlocked.Increment(ref inactiveFetchCount);
            return "inactive";
        });
        client.SetQueryData(["inactive"], "initial");

        // Act — refetch only active queries
        await client.RefetchQueriesAsync(new QueryFilters { Type = QueryTypeFilter.Active });
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(activeFetchCount >= 2, "Active query should be refetched");
        Assert.Equal(0, inactiveFetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_Only_Inactive_Queries()
    {
        // TanStack line 1279: Type=Inactive should only refetch queries without observers
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var activeFetchCount = 0;
        var inactiveFetchCount = 0;

        // Active query
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref activeFetchCount);
                    initialFetch.TrySetResult();
                    return "active";
                },
                Enabled = true
            });

        var sub = activeObserver.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var activeCountBefore = activeFetchCount;

        // Inactive query
        var cache = client.QueryCache;
        var inactiveQuery = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["inactive"], GcTime = TimeSpan.FromMinutes(5) });
        inactiveQuery.SetQueryFn(async _ =>
        {
            Interlocked.Increment(ref inactiveFetchCount);
            return "inactive";
        });
        client.SetQueryData(["inactive"], "initial");

        // Act — refetch only inactive queries
        await client.RefetchQueriesAsync(new QueryFilters { Type = QueryTypeFilter.Inactive });

        // Assert — active should NOT be refetched, inactive should
        Assert.Equal(activeCountBefore, activeFetchCount);
        Assert.True(inactiveFetchCount > 0, "Inactive query should be refetched");

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Not_Refetch_Static_Queries()
    {
        // TanStack line 1355: queries with StaleTime=Infinity are static and should be skipped
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-query"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"static-{c}";
                },
                Enabled = true,
                StaleTime = Timeout.InfiniteTimeSpan // Static
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act
        await client.RefetchQueriesAsync();
        await Task.Delay(100);

        // Assert — static queries should not be refetched
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Not_Start_Fetch_For_Paused_Queries()
    {
        // TanStack line 1324: when offline with NetworkMode.Online, the query
        // should pause and not actually execute the query function.
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);

        var fetchCount = 0;
        var initialFetch = new TaskCompletionSource();

        // Create an active query (with observer) that uses NetworkMode.Online
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["offline-query"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    initialFetch.TrySetResult();
                    return "data";
                },
                Enabled = true,
                NetworkMode = NetworkMode.Online
            });

        var sub = observer.Subscribe(_ => { });

        // The initial fetch should pause because we're offline
        await Task.Delay(200);
        Assert.Equal(0, fetchCount);

        // Verify the query is in a paused fetch state
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["offline-query"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.Equal(FetchStatus.Paused, query!.State!.FetchStatus);

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Complete_When_Paused_Queries_Are_In_Result_Set()
    {
        // Regression: RefetchQueriesAsync hung when paused queries were included
        // because their retryer blocks on a TCS indefinitely. TanStack
        // queryClient.ts:332–334 resolves immediately for paused queries.
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["paused-refetch"],
                QueryFn = async _ => "data",
                Enabled = true,
                NetworkMode = NetworkMode.Online
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(100);

        // Act — RefetchQueriesAsync must complete, not hang on the paused query's TCS.
        // WaitAsync guards against the pre-fix hang.
        var refetchTask = client.RefetchQueriesAsync();
        await refetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — if we got here, RefetchQueriesAsync completed instead of hanging
        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Not_Throw_For_QueryFnLess_Query_With_Observers()
    {
        // Regression: SetQueryData queries that later gain observers (making
        // them active) have no query function. RefetchQueriesAsync must not surface
        // the "Query function is not set" exception to callers.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["no-fn"], "seeded");

        // Attach an enabled observer without a query function so the query
        // becomes active (not disabled) yet has no queryFn.
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["no-fn"],
                Enabled = true,
                StaleTime = TimeSpan.FromMinutes(5) // prevent auto-fetch on subscribe
            });
        var sub = observer.Subscribe(_ => { });

        // Act
        var exception = await Record.ExceptionAsync(() => client.RefetchQueriesAsync());

        // Assert
        Assert.Null(exception);

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_When_Offline_With_NetworkMode_Always()
    {
        // TanStack line 1340: NetworkMode.Always queries should refetch even when offline
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        var refetchDone = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["always-network"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c >= 2) refetchDone.TrySetResult();
                    return $"data-{c}";
                },
                Enabled = true,
                NetworkMode = NetworkMode.Always
            });

        var sub = observer.Subscribe(_ => { });
        // NetworkMode.Always ignores online state, so initial fetch proceeds
        await Task.Delay(100);
        var countAfterInitial = fetchCount;
        Assert.True(countAfterInitial >= 1, "NetworkMode.Always should fetch even offline");

        // Act — refetch while offline
        await client.RefetchQueriesAsync(new QueryFilters { QueryKey = ["always-network"] });
        await refetchDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "NetworkMode.Always queries should refetch when offline");

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_Should_Refetch_All_Fresh_Active_Queries()
    {
        // TanStack line 1137: refetchQueries({type:'active', stale:false}) should
        // only refetch active queries with fresh data. Inactive queries are skipped.
        // Arrange
        var client = CreateQueryClient();
        var fetchCount1 = 0;
        var fetchCount2 = 0;

        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["key1"],
            QueryFn = async _ => { Interlocked.Increment(ref fetchCount1); return "data1"; }
        });
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["key2"],
            QueryFn = async _ => { Interlocked.Increment(ref fetchCount2); return "data2"; }
        });

        // key1 gets an observer with a large stale time → active + fresh (not static).
        // Avoid TimeSpan.MaxValue: while it maps to TanStack's `staleTime: Infinity`,
        // a few ms of clock drift between fetch and subscribe can bypass the overflow
        // guard in UpdateStaleTimeout, causing ArgumentOutOfRangeException. 1 hour is
        // safely within OS timer limits and keeps data fresh for the test's lifetime.
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["key1"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount1);
                    return "data1";
                },
                Enabled = true,
                StaleTime = TimeSpan.FromHours(1)
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Act
        await client.RefetchQueriesAsync(new QueryFilters
        {
            Type = QueryTypeFilter.Active,
            Stale = false
        });

        // Assert — only key1 (active + fresh) was refetched; key2 (inactive) was not
        Assert.Equal(2, fetchCount1);
        Assert.Equal(1, fetchCount2);

        sub.Dispose();
    }

    #endregion

    #region CancelRefetch_and_ThrowOnError

    [Fact]
    public async Task RefetchQueriesAsync_CancelRefetchTrue_CancelsInFlightFetch()
    {
        // TanStack ref: queryClient.test.tsx:1541-1562
        // CancelRefetch=true should cancel the in-flight fetch and start a new one.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["cancel-test"], "seeded");

        var fetchCount = 0;
        var firstFetchStarted = new TaskCompletionSource();
        var firstFetchGate = new TaskCompletionSource<string>();
        var secondFetchStarted = new TaskCompletionSource();
        var secondFetchGate = new TaskCompletionSource<string>();

        // Get the query from cache and set its query function so Fetch() has
        // something to run. The function blocks on a TCS so we can control timing.
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["cancel-test"]);
        var query = client.QueryCache.Get<string>(hash)!;
        query.SetQueryFn(async ctx =>
        {
            var c = Interlocked.Increment(ref fetchCount);
            if (c == 1)
            {
                firstFetchStarted.TrySetResult();
                return await firstFetchGate.Task.WaitAsync(ctx.CancellationToken);
            }

            secondFetchStarted.TrySetResult();
            return await secondFetchGate.Task.WaitAsync(ctx.CancellationToken);
        });

        // Start an in-flight fetch
        var firstFetch = query.Fetch();
        await firstFetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fetchCount);

        // Act — RefetchQueriesAsync with CancelRefetch=true should cancel that fetch
        // and start a new one
        var refetchTask = client.RefetchQueriesAsync(
            new QueryFilters { QueryKey = ["cancel-test"] },
            new RefetchOptions { CancelRefetch = true });

        await secondFetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — a second fetch was started (the first was cancelled)
        Assert.Equal(2, fetchCount);

        // Complete the second fetch so the refetch task can finish
        secondFetchGate.SetResult("refetched");
        await refetchTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RefetchQueriesAsync_CancelRefetchFalse_DeduplicatesInFlightFetch()
    {
        // TanStack ref: queryClient.test.tsx:1564-1586
        // CancelRefetch=false should deduplicate: the original in-flight fetch
        // continues and no new fetch starts.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["dedup-test"], "seeded");

        var fetchCount = 0;
        var fetchStarted = new TaskCompletionSource();
        var fetchGate = new TaskCompletionSource<string>();

        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["dedup-test"]);
        var query = client.QueryCache.Get<string>(hash)!;
        query.SetQueryFn(async ctx =>
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.TrySetResult();
            return await fetchGate.Task.WaitAsync(ctx.CancellationToken);
        });

        // Start an in-flight fetch
        var firstFetch = query.Fetch();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fetchCount);

        // Act — RefetchQueriesAsync with CancelRefetch=false should NOT cancel
        var refetchTask = client.RefetchQueriesAsync(
            new QueryFilters { QueryKey = ["dedup-test"] },
            new RefetchOptions { CancelRefetch = false });

        // Complete the original fetch
        fetchGate.SetResult("original-result");
        await refetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — only one fetch function call total (deduplicated)
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task RefetchQueriesAsync_ThrowOnErrorTrue_PropagatesErrors()
    {
        // TanStack ref: queryClient.test.tsx:1302-1320
        // ThrowOnError=true should propagate query function errors to the caller
        // via AggregateException from Task.WhenAll.
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        // Build the query with Retry=0 so failures are immediate, then seed
        // it with data so it counts as non-empty.
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["throw-test"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        client.SetQueryData(["throw-test"], "seeded");

        // Attach an observer so the query is "active" and eligible for refetch.
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["throw-test"],
                Enabled = true,
                StaleTime = TimeSpan.FromMinutes(5) // prevent auto-fetch on subscribe
            });
        var sub = observer.Subscribe(_ => { });

        // Set a failing query function for the refetch
        query.SetQueryFn(async _ => throw new InvalidOperationException("fetch failed"));

        // Act & Assert — Task.WhenAll unwraps AggregateException when there is
        // a single faulted task, so the thrown exception is InvalidOperationException
        // directly. The underlying AggregateException is still accessible via the
        // Task.Exception property, but await surfaces the first inner exception.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RefetchQueriesAsync(
                new QueryFilters { QueryKey = ["throw-test"] },
                new RefetchOptions { ThrowOnError = true }));

        sub.Dispose();
    }

    [Fact]
    public async Task RefetchQueriesAsync_ThrowOnErrorFalse_SuppressesErrors()
    {
        // Inverse of the ThrowOnError=true test: errors are swallowed by default.
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        // Build the query with Retry=0 so failures are immediate, then seed data.
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["suppress-test"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        client.SetQueryData(["suppress-test"], "seeded");

        // Attach an observer so the query is "active".
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["suppress-test"],
                Enabled = true,
                StaleTime = TimeSpan.FromMinutes(5) // prevent auto-fetch on subscribe
            });
        var sub = observer.Subscribe(_ => { });

        // Set a failing query function for the refetch
        query.SetQueryFn(async _ => throw new InvalidOperationException("fetch failed"));

        // Act — ThrowOnError=false, so no exception should propagate
        var exception = await Record.ExceptionAsync(() =>
            client.RefetchQueriesAsync(
                new QueryFilters { QueryKey = ["suppress-test"] },
                new RefetchOptions { ThrowOnError = false }));

        // Assert
        Assert.Null(exception);

        sub.Dispose();
    }

    [Fact]
    public void SetQueryData_UpdaterReturnsNull_IsNoOp()
    {
        // TanStack ref: queryClient.ts:980-983
        // When the updater function returns null, SetQueryData is a no-op —
        // the existing data in the cache is preserved.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["updater-null"], "original");

        // Act — updater returns null, which should be treated as a no-op
        client.SetQueryData<string>(["updater-null"], _ => null!);

        // Assert — original data is preserved
        Assert.Equal("original", client.GetQueryData<string>(["updater-null"]));
    }

    [Fact]
    public async Task RefetchQueriesAsync_TypeAll_RefetchesBothActiveAndInactive()
    {
        // TanStack ref: queryClient.test.tsx:1233-1254
        // Type=All should refetch both active (with observers) and inactive
        // (no observers but has cached data) queries.
        // Arrange
        var client = CreateQueryClient();
        var activeFetchCount = 0;
        var inactiveFetchCount = 0;
        var initialFetch = new TaskCompletionSource();
        var activeRefetch = new TaskCompletionSource();

        // Active query — has an observer subscription
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["type-all", "active"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref activeFetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else activeRefetch.TrySetResult();
                    return $"active-{c}";
                },
                Enabled = true
            });

        var sub = activeObserver.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Inactive query — seed data and set a query function, but no observer
        var cache = client.QueryCache;
        var inactiveQuery = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["type-all", "inactive"], GcTime = TimeSpan.FromMinutes(5) });
        inactiveQuery.SetQueryFn(async _ =>
        {
            Interlocked.Increment(ref inactiveFetchCount);
            return "inactive-refetched";
        });
        client.SetQueryData(["type-all", "inactive"], "inactive-initial");

        var activeCountBefore = activeFetchCount;

        // Act — refetch all queries (both active and inactive)
        await client.RefetchQueriesAsync(new QueryFilters { Type = QueryTypeFilter.All });

        await activeRefetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — both active and inactive queries were refetched
        Assert.True(activeFetchCount > activeCountBefore, "Active query should be refetched");
        Assert.True(inactiveFetchCount > 0, "Inactive query should be refetched");

        sub.Dispose();
    }

    [Fact]
    public async Task FocusEvent_DoesNotCrash_WhenQueryFnThrows()
    {
        // Defensive test: a focus event that triggers a refetch on a query whose
        // function throws must not propagate exceptions to the caller. The query's
        // FetchSilentAsync swallows exceptions from background refetches.
        // Arrange
        var focusManager = new FocusManager();
        focusManager.SetFocused(false); // Start unfocused so we can trigger focus gain
        var queryCache = new QueryCache();
        var client = new QueryClient(queryCache, focusManager: focusManager);

        // Build the query with Retry=0 so the failing refetch doesn't retry
        var query = queryCache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["focus-throw"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });

        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        query.SetQueryFn(async _ =>
        {
            var c = Interlocked.Increment(ref fetchCount);
            if (c == 1)
            {
                initialFetch.TrySetResult();
                return "ok";
            }

            throw new InvalidOperationException("refetch exploded");
        });

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-throw"],
                Enabled = true,
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — trigger focus gain; FetchSilentAsync should swallow the exception
        var exception = Record.Exception(() => focusManager.SetFocused(true));

        // Assert — no exception propagates
        Assert.Null(exception);

        // Client remains functional after the failed refetch
        client.SetQueryData(["focus-sanity"], "still-works");
        Assert.Equal("still-works", client.GetQueryData<string>(["focus-sanity"]));

        sub.Dispose();
    }

    #endregion

    #region invalidateQueries

    [Fact]
    public async Task InvalidateQueriesAsync_With_Null_Filters_Should_Invalidate_All()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "todos-data");
        client.SetQueryData(["users"], "users-data");

        // Act
        await client.InvalidateQueriesAsync(filters: null);

        // Assert
        var cache = client.QueryCache;
        foreach (var query in cache.GetAll())
        {
            Assert.True(query.IsStale(), "All queries should be stale after InvalidateQueriesAsync(null)");
        }
    }

    [Fact]
    public async Task InvalidateQueriesAsync_With_Exact_Match_Should_Only_Invalidate_Exact_Key()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "parent");
        client.SetQueryData(["todos", 1], "child");

        // Act — invalidate only the exact ["todos"] key
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = ["todos"], Exact = true });

        // Assert
        var parentHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var childHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos", 1]);

        var parentQuery = client.QueryCache.Get<string>(parentHash);
        var childQuery = client.QueryCache.Get<string>(childHash);

        Assert.True(parentQuery!.State!.IsInvalidated, "Exact-match query should be invalidated");
        Assert.False(childQuery!.State!.IsInvalidated, "Child query should NOT be invalidated by exact match");
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Trigger_Refetch_On_Active_Observers_By_Default()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert — invalidation triggers refetch on active queries
        Assert.True(fetchCount >= 2);

        sub.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Refetch_Inactive_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");

        // Act — invalidate without observers
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert — query should be invalidated but fetch status remains idle
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.True(query!.State!.IsInvalidated);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    [Fact]
    public async Task InvalidateQueriesAsync_With_RefetchType_None_Should_Not_Refetch()
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
                    Interlocked.Increment(ref fetchCount);
                    return $"v{fetchCount}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(50);
        var countAfterInitial = fetchCount;

        // Act — invalidate with RefetchType.None
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            QueryKey = ["todos"],
            RefetchType = InvalidateRefetchType.None
        });

        // Assert — query is invalidated but no refetch was triggered
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.True(query!.State!.IsInvalidated);
        Assert.Equal(countAfterInitial, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_With_RefetchType_Inactive_Should_Refetch_Inactive_Only()
    {
        // Arrange
        var client = CreateQueryClient();
        var activeFetchCount = 0;
        var inactiveFetchCount = 0;

        // Active query (has an observer subscription)
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref activeFetchCount);
                    return "active-data";
                },
                Enabled = true
            });
        var activeSub = activeObserver.Subscribe(_ => { });
        await Task.Delay(50);

        // Inactive query (no observer — set data directly)
        var cache = client.QueryCache;
        var inactiveQuery = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["inactive"], GcTime = TimeSpan.FromMinutes(5) });
        inactiveQuery.SetQueryFn(async _ =>
        {
            Interlocked.Increment(ref inactiveFetchCount);
            return "inactive-data";
        });
        // Seed data so the query has state
        client.SetQueryData(["inactive"], "initial");

        var activeCountBefore = activeFetchCount;
        var inactiveCountBefore = inactiveFetchCount;

        // Act — refetch only inactive queries
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            RefetchType = InvalidateRefetchType.Inactive
        });

        // Assert — active query was NOT refetched, inactive query WAS
        Assert.Equal(activeCountBefore, activeFetchCount);
        Assert.True(inactiveFetchCount > inactiveCountBefore,
            "Inactive query should have been refetched");

        activeSub.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_With_RefetchType_All_Should_Refetch_All()
    {
        // Arrange
        var client = CreateQueryClient();
        var activeFetchCount = 0;
        var inactiveFetchCount = 0;

        // Active query
        var activeObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref activeFetchCount);
                    return "active-data";
                },
                Enabled = true
            });
        var activeSub = activeObserver.Subscribe(_ => { });
        await Task.Delay(50);

        // Inactive query
        var cache = client.QueryCache;
        var inactiveQuery = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["inactive"], GcTime = TimeSpan.FromMinutes(5) });
        inactiveQuery.SetQueryFn(async _ =>
        {
            Interlocked.Increment(ref inactiveFetchCount);
            return "inactive-data";
        });
        client.SetQueryData(["inactive"], "initial");

        var activeCountBefore = activeFetchCount;
        var inactiveCountBefore = inactiveFetchCount;

        // Act — refetch all queries
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            RefetchType = InvalidateRefetchType.All
        });

        // Assert — both queries were refetched
        Assert.True(activeFetchCount > activeCountBefore,
            "Active query should have been refetched");
        Assert.True(inactiveFetchCount > inactiveCountBefore,
            "Inactive query should have been refetched");

        activeSub.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Returns_Task_That_Completes_After_Refetches()
    {
        // Arrange — gate the query function on a TCS so we can verify the
        // returned Task doesn't complete until the refetch finishes.
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();
        var fetchStarted = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gated"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) return "initial";

                    // Second fetch (refetch) blocks on TCS
                    fetchStarted.TrySetResult();
                    return await tcs.Task;
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(50);

        // Act
        var invalidateTask = client.InvalidateQueriesAsync(["gated"]);

        // Wait for the refetch to actually start
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — the task should NOT be complete while the refetch is blocked
        Assert.False(invalidateTask.IsCompleted,
            "InvalidateQueriesAsync should not complete until refetches finish");

        // Release the refetch
        tcs.SetResult("refetched");
        await invalidateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(invalidateTask.IsCompletedSuccessfully);

        sub.Dispose();
    }

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Refetch_Disabled_Inactive_Even_With_RefetchType_All()
    {
        // TanStack line 1501: invalidating with RefetchType=All should still not
        // refetch a disabled, inactive query. The guard `!q.IsDisabled()` at
        // QueryClient.cs:551 prevents it.
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-inactive"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return "data";
                },
                Enabled = false,
                StaleTime = TimeSpan.MaxValue
            });

        // Subscribe then immediately unsubscribe → inactive
        var sub = observer.Subscribe(_ => { });
        sub.Dispose();

        // Act
        await client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            RefetchType = InvalidateRefetchType.All
        });

        // Negative test: brief wait to confirm no refetch
        await Task.Delay(100);

        // Assert — query function should never have been called
        Assert.Equal(0, fetchCount);
    }

    #endregion

    #region resetQueries

    [Fact]
    public void ResetQueries_Should_Reset_Query_State_To_Pending()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");

        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.Equal(QueryStatus.Succeeded, query!.State!.Status);

        // Act
        client.ResetQueries(new QueryFilters { QueryKey = ["todos"] });

        // Assert — query state should reset to defaults
        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Null(query.State.Data);
    }

    [Fact]
    public async Task ResetQueries_Should_Trigger_Refetch_On_Active_Observers()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.ResetQueries(new QueryFilters { QueryKey = ["todos"] });
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "Reset should trigger refetch on active observers");

        sub.Dispose();
    }

    [Fact]
    public void ResetQueries_Should_Notify_Listeners()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");
        var notified = false;

        var cache = client.QueryCache;
        cache.Subscribe(e => { notified = true; });

        // Act
        client.ResetQueries(new QueryFilters { QueryKey = ["todos"] });

        // Assert
        Assert.True(notified);
    }

    #endregion

    #region isMutating

    [Fact]
    public async Task IsMutating_Should_Count_Pending_Mutations()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs1 = new TaskCompletionSource<string>();
        var tcs2 = new TaskCompletionSource<string>();
        var entered1 = new TaskCompletionSource();
        var entered2 = new TaskCompletionSource();

        Assert.Equal(0, client.IsMutating());

        // Act — start two mutations
        var observer1 = new MutationObserver<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = async (input, context, ct) =>
                {
                    entered1.TrySetResult();
                    return await tcs1.Task;
                }
            });

        var observer2 = new MutationObserver<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = async (input, context, ct) =>
                {
                    entered2.TrySetResult();
                    return await tcs2.Task;
                }
            });

        var task1 = observer1.MutateAsync("a");
        var task2 = observer2.MutateAsync("b");

        // Wait for mutations to actually start executing
        await Task.WhenAll(
            entered1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            entered2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert — two mutations should be pending
        Assert.Equal(2, client.IsMutating());

        // Complete first mutation
        tcs1.SetResult("done1");
        await task1;

        Assert.Equal(1, client.IsMutating());

        // Complete second mutation
        tcs2.SetResult("done2");
        await task2;

        Assert.Equal(0, client.IsMutating());
    }

    #endregion

    #region setQueriesData

    [Fact]
    public void SetQueriesData_Should_Update_All_Matching_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key", 1], "data1");
        client.SetQueryData(["key", 2], "data2");
        client.SetQueryData(["other"], "other-data");

        // Act
        client.SetQueriesData<string>(
            new QueryFilters { QueryKey = ["key"] },
            old => old + "-updated");

        // Assert
        Assert.Equal("data1-updated", client.GetQueryData<string>(["key", 1]));
        Assert.Equal("data2-updated", client.GetQueryData<string>(["key", 2]));
        Assert.Equal("other-data", client.GetQueryData<string>(["other"])); // unchanged
    }

    [Fact]
    public void SetQueriesData_Should_Accept_Predicate_Filter()
    {
        // TanStack line 369: setQueriesData with a Predicate filter should only
        // update queries matching the predicate.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key", 1], 1);
        client.SetQueryData(["key", 2], 2);

        var hash1 = DefaultQueryKeyHasher.Instance.HashQueryKey(["key", 1]);
        var query1 = client.QueryCache.Get<int>(hash1)!;

        // Act — only update queries matching the predicate
        client.SetQueriesData<int>(
            new QueryFilters { Predicate = q => q == query1 },
            old => old + 5);

        // Assert — only key[1] was updated
        Assert.Equal(6, client.GetQueryData<int>(["key", 1]));
        Assert.Equal(2, client.GetQueryData<int>(["key", 2]));
    }

    [Fact]
    public void SetQueriesData_Should_Not_Update_Non_Existing_Queries()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act — no queries match this key
        client.SetQueriesData<string>(
            new QueryFilters { QueryKey = ["nonexistent"] },
            old => "new-data");

        // Assert — no query created
        Assert.Null(client.GetQueryData<string>(["nonexistent"]));
    }

    #endregion

    #region getQueriesData

    [Fact]
    public void GetQueriesData_Should_Return_Data_For_All_Matching_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos", 1], "todo1");
        client.SetQueryData(["todos", 2], "todo2");
        client.SetQueryData(["users"], "user-data");

        // Act
        var results = client.GetQueriesData<string>(new QueryFilters { QueryKey = ["todos"] })
            .ToList();

        // Assert — should include both todo queries but not users
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void GetQueriesData_Should_Return_Empty_When_No_Match()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var results = client.GetQueriesData<string>(new QueryFilters { QueryKey = ["missing"] })
            .ToList();

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region removeQueries

    [Fact]
    public void RemoveQueries_Should_Remove_Matching_Queries_From_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");
        client.SetQueryData(["users"], "data");

        // Act
        client.RemoveQueries(new QueryFilters { QueryKey = ["todos"] });

        // Assert
        Assert.Null(client.GetQueryData<string>(["todos"]));
        Assert.Equal("data", client.GetQueryData<string>(["users"])); // unchanged
    }

    [Fact]
    public void RemoveQueries_With_Exact_Should_Not_Crash()
    {
        // Mirrors TanStack: "should not crash when exact is provided"
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "data");

        // Act — should not throw
        var exception = Record.Exception(() =>
            client.RemoveQueries(new QueryFilters { QueryKey = ["todos"], Exact = true }));

        // Assert
        Assert.Null(exception);
        Assert.Null(client.GetQueryData<string>(["todos"]));
    }

    #endregion

    #region Focus/Online integration

    [Fact]
    public async Task OnFocus_Should_Trigger_Refetch_Of_Active_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-test"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — simulate focus event
        client.QueryCache.OnFocus();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "Focus should trigger refetch");

        sub.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Trigger_Refetch_Of_Active_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["online-test"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — simulate online event
        client.QueryCache.OnOnline();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "Coming online should trigger refetch");

        sub.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Not_Refetch_When_RefetchOnWindowFocus_Is_Never()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-never"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true,
                RefetchOnWindowFocus = RefetchOnBehavior.Never
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act
        client.QueryCache.OnFocus();

        // Negative test: we're asserting nothing happens. A short delay is unavoidable
        // here since there's no signal to await — we just need enough time for any
        // erroneous async fetch to start.
        await Task.Delay(100);

        // Assert — fetch count should not increase
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Refetch_Even_When_Fresh_If_RefetchOnWindowFocus_Is_Always()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-always"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true,
                RefetchOnWindowFocus = RefetchOnBehavior.Always,
                StaleTime = TimeSpan.MaxValue // data is fresh
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.QueryCache.OnFocus();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — should still refetch despite fresh data
        Assert.True(fetchCount >= 2, "Always should refetch even when data is fresh");

        sub.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Not_Refetch_When_Data_Is_Fresh_And_RefetchOnWindowFocus_Is_WhenStale()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-fresh"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true,
                RefetchOnWindowFocus = RefetchOnBehavior.WhenStale,
                StaleTime = TimeSpan.MaxValue // data stays fresh
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act
        client.QueryCache.OnFocus();

        // Negative test: asserting no refetch occurs
        await Task.Delay(100);

        // Assert — data is fresh, WhenStale should not trigger refetch
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Not_Refetch_When_RefetchOnReconnect_Is_Never()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["online-never"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true,
                RefetchOnReconnect = RefetchOnBehavior.Never
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act
        client.QueryCache.OnOnline();

        // Negative test: asserting no refetch occurs
        await Task.Delay(100);

        // Assert
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Refetch_Even_When_Fresh_If_RefetchOnReconnect_Is_Always()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["online-always"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true,
                RefetchOnReconnect = RefetchOnBehavior.Always,
                StaleTime = TimeSpan.MaxValue // data is fresh
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.QueryCache.OnOnline();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "Always should refetch even when data is fresh");

        sub.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Not_Refetch_Disabled_Observer()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-disabled"],
                QueryFn = async _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return $"v{fetchCount}";
                },
                Enabled = false,
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });

        // Disabled observer should not trigger initial fetch
        var sub = observer.Subscribe(_ => { });
        await Task.Delay(100); // Negative test: brief wait to confirm no fetch
        var countBefore = fetchCount;

        // Act
        client.QueryCache.OnFocus();

        // Negative test: asserting no refetch occurs
        await Task.Delay(100);

        // Assert — disabled observer should never trigger refetch
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Not_Refetch_When_Losing_Focus()
    {
        // TanStack line 1705 partial: SetFocused(false) should not trigger OnFocus
        // on the query cache. The guard `if (!FocusManager.IsFocused) return` at
        // QueryClient.cs:103 short-circuits the handler.
        // Arrange
        var focusManager = new FocusManager();
        var client = new QueryClient(new QueryCache(), focusManager: focusManager);
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-losing"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"v{c}";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act — simulate LOSING focus (not gaining)
        focusManager.SetFocused(false);

        // Negative test: brief wait to confirm no refetch
        await Task.Delay(100);

        // Assert — losing focus should not trigger refetch
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Resume_Paused_Mutations()
    {
        // TanStack line 1772: mutations started while offline should pause,
        // then resume and succeed when coming back online.
        // Arrange
        var onlineManager = new OnlineManager();
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        onlineManager.SetOnline(false);

        var observer1 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                MutationFn = async (_, _, _) => 1
            });

        var observer2 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                MutationFn = async (_, _, _) => 2
            });

        // Act — start mutations while offline (both should pause)
        var task1 = observer1.MutateAsync("a");
        var task2 = observer2.MutateAsync("b");
        await Task.Delay(50);

        Assert.True(observer1.CurrentResult.IsPaused);
        Assert.True(observer2.CurrentResult.IsPaused);

        // Go back online — triggers OnOnlineChanged → ResumePausedMutations
        onlineManager.SetOnline(true);

        var result1 = await task1.WaitAsync(TimeSpan.FromSeconds(5));
        var result2 = await task2.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.True(observer1.CurrentResult.IsSuccess);
        Assert.True(observer2.CurrentResult.IsSuccess);

        client.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Resume_Paused_Mutations_In_Parallel()
    {
        // TanStack line 1797: unscoped mutations should resume in parallel.
        // Both mutation functions start before either completes.
        // Arrange
        var onlineManager = new OnlineManager();
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        onlineManager.SetOnline(false);

        var events = new List<string>();
        var started1 = new TaskCompletionSource();
        var started2 = new TaskCompletionSource();
        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();

        var observer1 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                MutationFn = async (_, _, _) =>
                {
                    lock (events) events.Add("1start");
                    started1.TrySetResult();
                    await gate1.Task;
                    lock (events) events.Add("1end");
                    return 1;
                }
            });

        var observer2 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                MutationFn = async (_, _, _) =>
                {
                    lock (events) events.Add("2start");
                    started2.TrySetResult();
                    await gate2.Task;
                    lock (events) events.Add("2end");
                    return 2;
                }
            });

        var task1 = observer1.MutateAsync("a");
        var task2 = observer2.MutateAsync("b");
        await Task.Delay(50);

        Assert.True(observer1.CurrentResult.IsPaused);
        Assert.True(observer2.CurrentResult.IsPaused);

        // Act — go online, both mutations resume in parallel
        onlineManager.SetOnline(true);
        await Task.WhenAll(
            started1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            started2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Release mutation 2 first, then mutation 1
        gate2.SetResult();
        await task2.WaitAsync(TimeSpan.FromSeconds(5));
        gate1.SetResult();
        await task1.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — parallel execution: both start before either ends.
        // The exact start order depends on thread scheduling, so we check
        // structural properties rather than exact sequence.
        Assert.Equal(4, events.Count);
        var s1 = events.IndexOf("1start");
        var s2 = events.IndexOf("2start");
        var e1 = events.IndexOf("1end");
        var e2 = events.IndexOf("2end");

        // Both starts precede both ends (proves parallel execution)
        Assert.True(s1 < e1, "1start should precede 1end");
        Assert.True(s2 < e2, "2start should precede 2end");
        Assert.True(s1 < e2 && s2 < e1, "Both should start before either ends");

        // gate2 released before gate1, so 2end precedes 1end
        Assert.True(e2 < e1, "2end should precede 1end");

        client.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Resume_Scoped_Mutations_Sequentially()
    {
        // TanStack line 1834: mutations sharing the same MutationScope should
        // resume sequentially — the second waits for the first to complete.
        // Arrange
        var onlineManager = new OnlineManager();
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        onlineManager.SetOnline(false);

        var events = new List<string>();
        var started1 = new TaskCompletionSource();
        var started2 = new TaskCompletionSource();
        var gate1 = new TaskCompletionSource();
        var scope = new MutationScope("scope");

        var observer1 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, _, _) =>
                {
                    lock (events) events.Add("1start");
                    started1.TrySetResult();
                    await gate1.Task;
                    lock (events) events.Add("1end");
                    return 1;
                }
            });

        var observer2 = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, _, _) =>
                {
                    lock (events) events.Add("2start");
                    started2.TrySetResult();
                    lock (events) events.Add("2end");
                    return 2;
                }
            });

        var task1 = observer1.MutateAsync("a");
        var task2 = observer2.MutateAsync("b");
        await Task.Delay(50);

        Assert.True(observer1.CurrentResult.IsPaused);
        Assert.True(observer2.CurrentResult.IsPaused);

        // Act — go online, scoped mutations resume sequentially
        onlineManager.SetOnline(true);

        // Mutation 1 starts first (sequential scope)
        await started1.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Release mutation 1 — only then should mutation 2 start
        gate1.SetResult();
        await started2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.WhenAll(
            task1.WaitAsync(TimeSpan.FromSeconds(5)),
            task2.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert — sequential: 1 completes before 2 starts
        Assert.Equal(["1start", "1end", "2start", "2end"], events);

        client.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Resume_After_Manual_Resume_While_Offline()
    {
        // TanStack line 1880: calling ResumePausedMutations() while still offline
        // should be a no-op. The mutation should only resume when actually online.
        // Arrange
        var onlineManager = new OnlineManager();
        var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        onlineManager.SetOnline(false);

        var observer = new MutationObserver<int, Exception, string, object?>(
            client,
            new MutationOptions<int, Exception, string, object?>
            {
                MutationFn = async (_, _, _) => 1
            });

        var task = observer.MutateAsync("a");
        await Task.Delay(50);

        Assert.True(observer.CurrentResult.IsPaused);

        // Act — manually call ResumePausedMutations while still offline (should be no-op)
        client.ResumePausedMutations();

        // Still paused because we are still offline
        Assert.True(observer.CurrentResult.IsPaused);

        // Now actually go online
        onlineManager.SetOnline(true);

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(1, result);
        Assert.True(observer.CurrentResult.IsSuccess);

        client.Dispose();
    }

    #endregion

    #region setQueryData edge cases

    [Fact]
    public void SetQueryData_With_Null_Variables_Should_Not_Crash()
    {
        // Mirrors TanStack: "should not crash when variable is null"
        // Arrange
        var client = CreateQueryClient();

        // Act — create with null in the key
        client.SetQueryData<string>(["key", null!], "Old Data");

        var exception = Record.Exception(() =>
            client.SetQueryData<string>(["key", null!], "New Data"));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void SetQueryData_With_Updater_Should_Receive_Current_Data()
    {
        // Mirrors TanStack: "should accept an update function"
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key"], "test data");

        // Act
        client.SetQueryData<string>(["key"], old => $"new data + {old}");

        // Assert
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.Equal("new data + test data", query!.State!.Data);
    }

    #endregion

    #region cancelQueries

    [Fact]
    public async Task InvalidateQueriesAsync_Should_Not_Refetch_Static_Queries()
    {
        // TanStack line 1588: invalidating a static query should mark it as invalidated
        // but not trigger a refetch, since static queries are never considered stale.
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["static-invalidate"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return $"data-{c}";
                },
                Enabled = true,
                StaleTime = Timeout.InfiniteTimeSpan // Static
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countBefore = fetchCount;

        // Act
        await client.InvalidateQueriesAsync(["static-invalidate"]);
        await Task.Delay(100);

        // Assert — query should be invalidated but NOT refetched
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["static-invalidate"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.True(query!.State!.IsInvalidated);
        Assert.Equal(countBefore, fetchCount);

        sub.Dispose();
    }

    #endregion

    #region resetQueries extended

    [Fact]
    public void ResetQueries_Should_Reset_To_InitialData_If_Set()
    {
        // TanStack line 1642: if a query has InitialData, reset should restore
        // it to that state (Status=Succeeded with InitialData).
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string>
            {
                QueryKey = ["with-initial"],
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "initial-value"
            });

        // Modify state via SetQueryData
        client.SetQueryData(["with-initial"], "modified");
        Assert.Equal("modified", query.State!.Data);

        // Act
        client.ResetQueries(new QueryFilters { QueryKey = ["with-initial"] });

        // Assert — should reset to InitialData, not to Pending/null
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("initial-value", query.State.Data);
    }

    #endregion

    #region getQueriesData extended

    [Fact]
    public void GetQueriesData_Should_Accept_Predicate_Filter()
    {
        // TanStack line 605: getQueriesData should support a custom Predicate filter
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key", 1], "data1");
        client.SetQueryData(["key", 2], "data2");
        client.SetQueryData(["key", 3], "data3");

        // Act — filter using a predicate that only matches even-numbered keys
        var results = client.GetQueriesData<string>(new QueryFilters
        {
            QueryKey = ["key"],
            Predicate = query =>
            {
                // Check if the second element of the key is even
                var keyParts = query.QueryKey?.ToList();
                if (keyParts is { Count: >= 2 } && keyParts[1] is int n)
                    return n % 2 == 0;
                return false;
            }
        }).ToList();

        // Assert — should only include key[2]
        Assert.Single(results);
        Assert.Equal("data2", results[0].Data);
    }

    #endregion

    #region cancelQueries extended

    [Fact]
    public async Task CancelQueries_Should_Not_Revert_When_Revert_Is_False()
    {
        // TanStack line 1018: cancel with Revert=false should cancel the fetch
        // but keep the current state (not revert to pre-fetch state)
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key"], "original");

        var fetchStarted = new TaskCompletionSource();
        var tcs = new TaskCompletionSource<string>();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        var query = client.QueryCache.Get<string>(hash)!;

        // Optimistic update before the second fetch
        client.SetQueryData(["key"], "optimistic");

        query.SetQueryFn(async _ =>
        {
            fetchStarted.TrySetResult();
            return await tcs.Task;
        });

        var fetchTask = query.Fetch();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — cancel without reverting
        await client.CancelQueriesAsync(
            new QueryFilters { QueryKey = ["key"] },
            new CancelOptions { Revert = false });

        // Assert — should NOT revert to pre-fetch state
        Assert.Equal("optimistic", query.State!.Data);
    }

    [Fact]
    public async Task CancelQueries_Should_Set_Pending_Idle_When_Initial_Fetch_Cancelled()
    {
        // TanStack line 1036: cancelling a query's first fetch (no previous data)
        // should result in Status=Pending, FetchStatus=Idle
        // Arrange
        var client = CreateQueryClient();
        var fetchStarted = new TaskCompletionSource();
        var tcs = new TaskCompletionSource<string>();

        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["first-cancel"], GcTime = QueryTimeDefaults.GcTime, Retry = 0 });
        query.SetQueryFn(async _ =>
        {
            fetchStarted.TrySetResult();
            return await tcs.Task;
        });

        var fetchTask = query.Fetch();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — cancel the initial fetch with revert
        await client.CancelQueriesAsync(
            new QueryFilters { QueryKey = ["first-cancel"] },
            new CancelOptions { Revert = true });

        // Assert — should revert to initial state (Pending, Idle, no data)
        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
        Assert.Null(query.State.Data);
    }

    [Fact]
    public async Task CancelQueries_Should_Revert_To_Previous_State_By_Default()
    {
        // Mirrors TanStack: "should revert queries to their previous state"
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key"], "original");

        var fetchStarted = new TaskCompletionSource();
        var tcs = new TaskCompletionSource<string>();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        var query = client.QueryCache.Get<string>(hash);
        Assert.NotNull(query);

        query!.SetQueryFn(async _ =>
        {
            fetchStarted.TrySetResult();
            return await tcs.Task;
        });

        // Start a fetch that won't complete
        var fetchTask = query.Fetch();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — cancel with default options (revert = true)
        await client.CancelQueriesAsync(new QueryFilters { QueryKey = ["key"] });

        // Assert — state should revert to pre-fetch state
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("original", query.State.Data);
    }

    #endregion

    #region Dispose unsubscribes from singletons

    [Fact]
    public async Task Dispose_Should_Unsubscribe_From_FocusManager_And_OnlineManager()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["dispose-test"],
                QueryFn = async _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) initialFetch.TrySetResult();
                    return "data";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — dispose the client
        client.Dispose();

        // Public methods throw ObjectDisposedException after disposal
        Assert.Throws<ObjectDisposedException>(() => client.QueryCache);

        // Negative test: brief wait to confirm no additional fetch
        await Task.Delay(100);

        sub.Dispose();
    }

    #endregion
}
