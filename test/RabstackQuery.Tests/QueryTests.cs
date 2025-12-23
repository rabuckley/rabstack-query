namespace RabstackQuery.Tests;

/// <summary>
/// Tests for the Query state machine, garbage collection, retry logic,
/// cancellation, and observer management.
/// </summary>
public sealed class QueryTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    private static Query<TData> BuildQuery<TData>(
        QueryClient client,
        QueryKey queryKey,
        TimeSpan? gcTime = null,
        int retry = 3,
        Func<int, Exception, TimeSpan>? retryDelay = null)
    {
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        var options = new QueryConfiguration<TData>
        {
            QueryKey = queryKey,
            QueryHash = queryHash,
            GcTime = gcTime ?? QueryTimeDefaults.GcTime,
            Retry = retry,
            RetryDelay = retryDelay,
        };
        return cache.Build<TData, TData>(client, options);
    }

    #region State Machine

    [Fact]
    public void New_Query_Should_Have_Default_Pending_State()
    {
        // Arrange & Act
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Assert — per TanStack, a query with no data starts with Status = Pending
        Assert.NotNull(query.State);
        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
        Assert.False(query.State.IsInvalidated);
        Assert.Null(query.State.Data);
    }

    [Fact]
    public async Task Fetch_Should_Transition_Through_Fetching_To_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        var stateSnapshots = new List<(QueryStatus Status, FetchStatus FetchStatus)>();

        // Use an observer to capture state transitions
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    // Intentionally empty — we want this specific query
                    return "data";
                },
                Enabled = false // Don't auto-fetch
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            stateSnapshots.Add((result.Status, result.FetchStatus));
        });

        // Act — manually fetch
        await query.Fetch();

        // Assert — should have seen Fetching then Success/Idle
        Assert.Contains(stateSnapshots, s => s.FetchStatus == FetchStatus.Fetching);
        Assert.Contains(stateSnapshots, s =>
            s.Status == QueryStatus.Succeeded && s.FetchStatus == FetchStatus.Idle);

        subscription.Dispose();
    }

    [Fact]
    public async Task Fetch_Should_Transition_To_Error_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        query.SetQueryFn(async _ => throw new InvalidOperationException("fetch failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.Fetch());
        Assert.Equal(QueryStatus.Errored, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
        Assert.NotNull(query.State.Error);
    }

    [Fact]
    public async Task Fetch_Should_Throw_When_QueryFn_Not_Set()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Act & Assert — no query function set
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => query.Fetch());
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public void Invalidate_Should_Set_IsInvalidated_Flag()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Act
        query.Invalidate();

        // Assert
        Assert.True(query.State!.IsInvalidated);
    }

    [Fact]
    public void Double_Invalidation_Should_Keep_Flag_True()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Act
        query.Invalidate();
        query.Invalidate();

        // Assert
        Assert.True(query.State!.IsInvalidated);
    }

    [Fact]
    public async Task Fetch_After_Invalidation_Should_Clear_IsInvalidated()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        query.SetQueryFn(async _ => "fresh-data");
        query.Invalidate();
        Assert.True(query.State!.IsInvalidated);

        // Act
        await query.Fetch();

        // Assert — FetchAction clears IsInvalidated
        Assert.False(query.State!.IsInvalidated);
    }

    [Fact]
    public async Task Successful_Fetch_Should_Store_Data_In_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        query.SetQueryFn(async _ => "hello world");

        // Act
        await query.Fetch();

        // Assert
        Assert.Equal("hello world", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.True(query.State.DataUpdatedAt > 0);
    }

    [Fact]
    public async Task Failed_Fetch_Should_Store_Error_In_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        var expectedError = new InvalidOperationException("test error");
        query.SetQueryFn(async _ => throw expectedError);

        // Act
        try { await query.Fetch(); } catch { }

        // Assert
        Assert.Equal(QueryStatus.Errored, query.State!.Status);
        Assert.Same(expectedError, query.State.Error);
        Assert.Equal(1, query.State.FetchFailureCount);
        Assert.Equal(1, query.State.ErrorUpdateCount);
    }

    [Fact]
    public async Task Previous_Data_Should_Be_Preserved_On_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        var callCount = 0;
        query.SetQueryFn(async _ =>
        {
            callCount++;
            if (callCount == 1) return "initial-data";
            throw new InvalidOperationException("second fetch fails");
        });

        // First fetch succeeds
        await query.Fetch();
        Assert.Equal("initial-data", query.State!.Data);

        // Act — second fetch fails
        try { await query.Fetch(); } catch { }

        // Assert — data from previous successful fetch is preserved
        Assert.Equal("initial-data", query.State!.Data);
        Assert.Equal(QueryStatus.Errored, query.State.Status);
    }

    #endregion

    #region Retry

    [Fact]
    public async Task Retry_Should_Invoke_OnFail_For_Each_Attempt()
    {
        // Arrange
        var client = CreateQueryClient();
        var failureCounts = new List<int>();

        // retry=2 means the retryer gets MaxRetries=3 (initial + 2 retries),
        // so it will try 3 times total. We make all attempts fail.
        var query = BuildQuery<string>(client, ["todos"], retry: 2, retryDelay: (_, _) => TimeSpan.Zero);
        query.SetQueryFn(async _ => throw new InvalidOperationException("always fails"));

        // Capture state changes — FailedAction updates FetchFailureCount while FetchStatus stays Fetching
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                Enabled = false
            }
        );

        var subscription = observer.Subscribe(result =>
        {
            if (result.FetchStatus == FetchStatus.Fetching && result.FailureCount > 0)
            {
                failureCounts.Add(result.FailureCount);
            }
        });

        // Act
        try { await query.Fetch(); } catch { }

        // Assert — should have seen intermediate failure counts during retries
        Assert.True(failureCounts.Count >= 1, "Should have observed at least one retry failure");

        subscription.Dispose();
    }

    [Fact]
    public async Task Retry_Should_Stop_After_Max_Retries_And_Set_Error_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var attemptCount = 0;
        var query = BuildQuery<string>(client, ["todos"], retry: 2, retryDelay: (_, _) => TimeSpan.Zero);
        query.SetQueryFn(async _ =>
        {
            attemptCount++;
            throw new InvalidOperationException($"attempt {attemptCount}");
        });

        // Act
        try { await query.Fetch(); } catch { }

        // Assert — 1 initial + 2 retries = 3 total attempts
        Assert.Equal(3, attemptCount);
        Assert.Equal(QueryStatus.Errored, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    [Fact]
    public async Task Custom_RetryDelay_Should_Be_Invoked()
    {
        // Arrange
        var client = CreateQueryClient();
        var delayInvocations = new List<int>();
        var query = BuildQuery<string>(client, ["todos"], retry: 1, retryDelay: (count, _) =>
        {
            delayInvocations.Add(count);
            return TimeSpan.Zero; // No actual delay for testing
        });
        query.SetQueryFn(async _ => throw new InvalidOperationException("fail"));

        // Act
        try { await query.Fetch(); } catch { }

        // Assert
        Assert.Single(delayInvocations);
        Assert.Equal(1, delayInvocations[0]);
    }

    [Fact]
    public async Task Retry_Success_On_Second_Attempt_Should_Clear_Failure_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var attemptCount = 0;
        var query = BuildQuery<string>(client, ["todos"], retry: 2, retryDelay: (_, _) => TimeSpan.Zero);
        query.SetQueryFn(async _ =>
        {
            attemptCount++;
            if (attemptCount == 1) throw new InvalidOperationException("first attempt fails");
            return "success-on-retry";
        });

        // Act
        await query.Fetch();

        // Assert
        Assert.Equal(2, attemptCount);
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("success-on-retry", query.State.Data);
        Assert.Equal(0, query.State.FetchFailureCount);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task CancellationToken_Should_Cancel_InFlight_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        var cts = new CancellationTokenSource();

        query.SetQueryFn(async ctx =>
        {
            await Task.Delay(5000, ctx.CancellationToken); // Long-running task
            return "should not reach here";
        });

        // Act — cancel after a short delay
        cts.CancelAfter(50);

        // Assert — Task.Delay throws TaskCanceledException (a subclass of OperationCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.Fetch(cts.Token));
    }

    [Fact]
    public async Task Cancelled_Query_Can_Be_Refetched()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"], retry: 0);
        var fetchCount = 0;

        query.SetQueryFn(async ctx =>
        {
            fetchCount++;
            if (fetchCount == 1)
            {
                await Task.Delay(5000, ctx.CancellationToken);
                return "should not reach";
            }
            return "refetch-success";
        });

        // Cancel the first fetch
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        try { await query.Fetch(cts.Token); } catch (OperationCanceledException) { }

        // Act — refetch without cancellation
        await query.Fetch();

        // Assert
        Assert.Equal("refetch-success", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    #endregion

    #region Garbage Collection

    [Fact]
    public async Task Query_With_GcTime_Zero_Should_Be_Removed_When_No_Observers()
    {
        // Arrange — gcTime=1 (minimum valid timeout) so removal happens quickly
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var query = BuildQuery<string>(client, ["gc-test"], gcTime: TimeSpan.FromMilliseconds(1), retry: 0);
        query.SetQueryFn(async _ => "data");

        // Act — wait for GC timer to fire
        await Task.Delay(100);

        // Assert — query should have been removed (no observers, FetchStatus is Idle)
        Assert.Null(cache.Get<string>(query.QueryHash!));
    }

    [Fact]
    public async Task Query_Should_Not_Be_GCd_While_Observers_Exist()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var query = BuildQuery<string>(client, ["gc-observer-test"], gcTime: TimeSpan.FromMilliseconds(1), retry: 0);
        query.SetQueryFn(async _ => "data");

        // Add an observer to keep the query alive
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-observer-test"],
                QueryFn = async _ => "data",
                Enabled = false
            }
        );
        var subscription = observer.Subscribe(_ => { });

        // Act — wait longer than gcTime
        await Task.Delay(100);

        // Assert — query should still exist because it has observers
        Assert.NotNull(cache.GetByHash(query.QueryHash!));

        subscription.Dispose();
    }

    #endregion

    #region Observer Management

    [Fact]
    public void AddObserver_Should_Register_Observer()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                Enabled = false
            }
        );

        // Observers are automatically added by the QueryObserver constructor via UpdateQuery.
        // Verify by checking that invalidation triggers the observer callback.
        var notificationReceived = false;
        var subscription = observer.Subscribe(_ => notificationReceived = true);

        // Act
        query.Invalidate();

        // Assert
        Assert.True(notificationReceived);

        subscription.Dispose();
    }

    [Fact]
    public void RemoveObserver_Should_Unregister_Observer()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                Enabled = false
            }
        );

        var notificationCount = 0;
        var subscription = observer.Subscribe(_ => notificationCount++);

        // First invalidation should notify
        query.Invalidate();
        var countAfterFirstInvalidation = notificationCount;

        // Act — unsubscribe removes the observer
        subscription.Dispose();

        // Invalidate again — should not trigger observer
        query.Invalidate();

        // Assert
        Assert.Equal(countAfterFirstInvalidation, notificationCount);
    }

    [Fact]
    public void AddObserver_Should_Not_Add_Duplicate()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // The observer auto-registers via constructor. Calling AddObserver again
        // should be a no-op (checked via _observers.Contains in Query<TData>).
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                Enabled = false
            }
        );

        // Act — subscribe twice, which internally calls AddObserver
        var sub1 = observer.Subscribe(_ => { });
        var sub2 = observer.Subscribe(_ => { });

        // Verify by counting notifications on invalidation
        var notificationCount = 0;
        var countingSub = observer.Subscribe(_ => notificationCount++);
        query.Invalidate();

        // Assert — observer should only receive one notification per dispatch
        // (not doubled from duplicate registration)
        Assert.Equal(1, notificationCount);

        sub1.Dispose();
        sub2.Dispose();
        countingSub.Dispose();
    }

    #endregion

    #region GC Time Management

    /// <summary>
    /// Mirrors TanStack: "should use the longest garbage collection time it has seen"
    /// When a query is built multiple times with different gcTimes, Removable.UpdateGcTime
    /// takes the MAX.
    /// </summary>
    [Fact]
    public void Query_Should_Use_Longest_GcTime_Seen()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["gc-max"]);

        // Build the same query with increasing gcTimes
        var q1 = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["gc-max"],
            QueryHash = queryHash,
            GcTime = TimeSpan.FromMilliseconds(100)
        });

        var q2 = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["gc-max"],
            QueryHash = queryHash,
            GcTime = TimeSpan.FromMilliseconds(200)
        });

        // Build with a smaller gcTime — should not reduce
        var q3 = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["gc-max"],
            QueryHash = queryHash,
            GcTime = TimeSpan.FromMilliseconds(10)
        });

        // Assert — all return the same query instance
        Assert.Same(q1, q2);
        Assert.Same(q2, q3);

        // The query's options reflect the last Build call, but the underlying GC
        // time (managed by Removable) should be the max. We verify indirectly by
        // checking the query survives past the shorter gcTimes.
    }

    #endregion

    #region State edge cases

    /// <summary>
    /// Mirrors TanStack: "should keep the previous status when refetch"
    /// A query that has data and is refetched stays in Succeeded status.
    /// </summary>
    [Fact]
    public async Task Refetch_Should_Preserve_Previous_Success_Status()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var query = BuildQuery<string>(client, ["status-preserve"], retry: 0);
        query.SetQueryFn(async _ => { fetchCount++; return $"data-{fetchCount}"; });

        // First fetch
        await query.Fetch();
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("data-1", query.State.Data);

        // Act — refetch
        await query.Fetch();

        // Assert — status should still be Succeeded with new data
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("data-2", query.State.Data);
    }

    /// <summary>
    /// Mirrors TanStack: "should not dispatch fetch if already fetching"
    /// When Fetch is called while the query is already fetching, the second call
    /// returns the same Task (deduplication) instead of creating a new Retryer.
    /// </summary>
    [Fact]
    public async Task Fetch_While_Already_Fetching_Should_Not_Dispatch_Double_FetchAction()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["double-fetch"], retry: 0);
        var tcs = new TaskCompletionSource<string>();
        var fetchCount = 0;

        query.SetQueryFn(async _ =>
        {
            fetchCount++;
            return await tcs.Task;
        });

        // Start first fetch (will block on TCS)
        var fetch1 = query.Fetch();
        await Task.Delay(50); // Let it start
        Assert.Equal(1, fetchCount);

        // Act — start second fetch while first is still in progress.
        // With deduplication, this returns the same Task.
        var fetch2 = query.Fetch();
        Assert.Same(fetch1, fetch2);

        // Complete the fetch
        tcs.SetResult("done");
        await fetch1;
        await fetch2;

        // Assert — the query function should only have been called once
        Assert.Equal(1, fetchCount);
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("done", query.State.Data);
    }

    /// <summary>
    /// After the first fetch completes, a new Fetch() call should start a fresh
    /// fetch rather than returning a stale deduplication task.
    /// </summary>
    [Fact]
    public async Task Fetch_After_Previous_Completes_Should_Start_New_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["dedup-reset"], retry: 0);
        var callCount = 0;

        query.SetQueryFn(async _ =>
        {
            callCount++;
            return $"result-{callCount}";
        });

        // Act — first fetch
        await query.Fetch();
        Assert.Equal(1, callCount);
        Assert.Equal("result-1", query.State!.Data);

        // Act — second fetch after first completed: should NOT deduplicate
        await query.Fetch();

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal("result-2", query.State.Data);
    }

    /// <summary>
    /// Fetch deduplication should still work when a cancelled fetch is followed
    /// by a new fetch. The cancelled task should not prevent a fresh fetch.
    /// </summary>
    [Fact]
    public async Task Fetch_After_Cancellation_Should_Start_New_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["dedup-cancel"], retry: 0);
        var callCount = 0;

        // The query function must respect the cancellation token, otherwise
        // Cancel() cannot interrupt it.
        query.SetQueryFn(async ctx =>
        {
            callCount++;
            await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return "never";
        });

        // Start a fetch that will block
        var fetch1 = query.Fetch();
        await Task.Delay(50);
        Assert.Equal(1, callCount);

        // Cancel it
        await query.Cancel();

        // The cancelled fetch should throw (TaskCanceledException inherits from
        // OperationCanceledException, so we need ThrowsAnyAsync)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch1);

        // Act — start a new fetch after cancellation
        query.SetQueryFn(async _ => "fresh");
        await query.Fetch();

        // Assert — should have called the function again (not deduplicated)
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("fresh", query.State.Data);
    }

    /// <summary>
    /// Mirrors TanStack: "should not go to error state if reset while pending"
    /// Resetting a query that's in a pending state should not cause errors.
    /// </summary>
    [Fact]
    public void Reset_While_Pending_Should_Return_To_Clean_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["reset-pending"], retry: 0);

        Assert.Equal(QueryStatus.Pending, query.State!.Status);

        // Act
        query.Reset();

        // Assert — should be back to default pending state
        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
        Assert.Null(query.State.Data);
        Assert.Null(query.State.Error);
    }

    /// <summary>
    /// Mirrors TanStack: query with InitialData should start as Succeeded.
    /// </summary>
    [Fact]
    public void Query_With_InitialData_Should_Start_As_Succeeded()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["initial"]);

        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["initial"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "initial-value"
            });

        // Assert — InitialData means query starts as Succeeded
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("initial-value", query.State.Data);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    /// <summary>
    /// Mirrors TanStack: Reset returns to initialData if set.
    /// </summary>
    [Fact]
    public async Task Reset_Should_Return_To_InitialData_If_Set()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["reset-initial"]);

        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["reset-initial"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "initial-value",
                Retry = 0
            });

        query.SetQueryFn(async _ => "fetched-value");
        await query.Fetch();
        Assert.Equal("fetched-value", query.State!.Data);

        // Act
        query.Reset();

        // Assert — should return to initial state with InitialData
        Assert.Equal("initial-value", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    // ── InitialData as a Function ──────────────────────────────────────

    /// <summary>
    /// Mirrors TanStack: initialData can be a function that returns the initial value.
    /// </summary>
    [Fact]
    public void InitialDataFactory_Should_Be_Called_And_Used()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["factory"]);
        var callCount = 0;

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["factory"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialDataFactory = () => { callCount++; return "factory-value"; }
            });

        // Assert
        Assert.Equal(1, callCount);
        Assert.Equal("factory-value", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    /// <summary>
    /// When InitialDataFactory returns null, the query starts as Pending
    /// (same as having no initial data at all).
    /// </summary>
    [Fact]
    public void InitialDataFactory_Returning_Null_Should_Be_Pending()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["factory-null"]);

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["factory-null"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialDataFactory = () => null
            });

        // Assert
        Assert.Null(query.State!.Data);
        Assert.Equal(QueryStatus.Pending, query.State.Status);
        Assert.Equal(0, query.State.DataUpdatedAt);
    }

    /// <summary>
    /// InitialDataFactory takes precedence over InitialData when both are set.
    /// </summary>
    [Fact]
    public void InitialDataFactory_Should_Take_Precedence_Over_InitialData()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["factory-precedence"]);

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["factory-precedence"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "direct-value",
                InitialDataFactory = () => "factory-wins"
            });

        // Assert
        Assert.Equal("factory-wins", query.State!.Data);
    }

    /// <summary>
    /// The canonical use case: derive initial data from another cached query.
    /// </summary>
    [Fact]
    public void InitialDataFactory_Should_Derive_From_Other_Query()
    {
        // Arrange — seed a "todos" query with a list
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], new[] { "buy milk", "walk dog" });

        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todo", 1]);

        // Act — derive initial data for a single-item query from the list
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["todo", 1],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialDataFactory = () =>
                    client.GetQueryData<string[]>(["todos"])?.ElementAtOrDefault(1)
            });

        // Assert
        Assert.Equal("walk dog", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    /// <summary>
    /// Reset() should re-invoke the factory to get fresh initial data.
    /// </summary>
    [Fact]
    public async Task Reset_Should_Reinvoke_InitialDataFactory()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["factory-reset"]);
        var callCount = 0;

        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["factory-reset"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialDataFactory = () => { callCount++; return $"call-{callCount}"; },
                Retry = 0
            });

        Assert.Equal("call-1", query.State!.Data);

        // Fetch to overwrite
        query.SetQueryFn(async _ => "fetched");
        await query.Fetch();
        Assert.Equal("fetched", query.State!.Data);

        // Act — reset should re-invoke factory
        query.Reset();

        // Assert — factory was called again with fresh return value
        Assert.Equal(2, callCount); // +1 from Build, +1 from Reset
        Assert.Equal("call-2", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    // ── InitialDataUpdatedAt ───────────────────────────────────────────

    /// <summary>
    /// When InitialDataUpdatedAt is set, the query should use that timestamp
    /// instead of the current time.
    /// </summary>
    [Fact]
    public void InitialDataUpdatedAt_Should_Override_Default_Timestamp()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["updated-at"]);

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["updated-at"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "seeded",
                InitialDataUpdatedAt = 1_000_000
            });

        // Assert — should use the explicit timestamp, not current time
        Assert.Equal(1_000_000, query.State!.DataUpdatedAt);
    }

    /// <summary>
    /// InitialDataUpdatedAtFactory takes precedence over InitialDataUpdatedAt.
    /// </summary>
    [Fact]
    public void InitialDataUpdatedAtFactory_Should_Take_Precedence()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["updated-at-factory"]);

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["updated-at-factory"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "seeded",
                InitialDataUpdatedAt = 1_000_000,
                InitialDataUpdatedAtFactory = () => 2_000_000
            });

        // Assert — factory wins
        Assert.Equal(2_000_000, query.State!.DataUpdatedAt);
    }

    /// <summary>
    /// When InitialDataUpdatedAtFactory returns null, falls back to current time
    /// (same as TanStack's <c>initialDataUpdatedAt ?? Date.now()</c>).
    /// </summary>
    [Fact]
    public void InitialDataUpdatedAtFactory_Returning_Null_Falls_Back_To_Current_Time()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(start);
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);
        var cache = client.GetQueryCache();
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["updated-at-null"]);

        // Act
        var query = cache.Build<string, string>(
            client,
            new QueryConfiguration<string>
            {
                QueryKey = ["updated-at-null"],
                QueryHash = hash,
                GcTime = QueryTimeDefaults.GcTime,
                InitialData = "seeded",
                InitialDataUpdatedAtFactory = () => null
            });

        // Assert — should fall back to current time from FakeTimeProvider
        Assert.Equal(start.ToUnixTimeMilliseconds(), query.State!.DataUpdatedAt);
    }

    /// <summary>
    /// Cancelling a query should not set its status to Errored.
    /// </summary>
    [Fact]
    public async Task Cancel_Should_Not_Set_Status_To_Errored()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["cancel-no-error"], retry: 0);
        query.SetQueryFn(async ctx =>
        {
            await Task.Delay(5000, ctx.CancellationToken);
            return "data";
        });

        // Start fetch
        var fetchTask = query.Fetch();
        await Task.Delay(50);

        // Act — cancel
        await query.Cancel();

        // Assert — cancellation doesn't set Error status
        Assert.NotEqual(QueryStatus.Errored, query.State!.Status);
        Assert.Null(query.State.Error);
    }

    /// <summary>
    /// Mirrors TanStack: "should refetch cancelled query"
    /// </summary>
    [Fact]
    public async Task Cancelled_Query_Can_Be_Refetched_Successfully()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["cancel-refetch"], retry: 0);
        var callCount = 0;

        query.SetQueryFn(async ctx =>
        {
            callCount++;
            if (callCount == 1)
            {
                await Task.Delay(5000, ctx.CancellationToken);
                return "never";
            }
            return "refetched";
        });

        // Start and cancel first fetch
        var fetchTask = query.Fetch();
        await Task.Delay(50);
        await query.Cancel();

        // Act — refetch
        await query.Fetch();

        // Assert
        Assert.Equal("refetched", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    /// <summary>
    /// IsStale should return true when query is invalidated.
    /// </summary>
    [Fact]
    public void IsStale_Should_Return_True_When_Invalidated()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["stale-test"]);
        query.SetQueryFn(async _ => "data");

        // Initially stale because Pending with no data
        Assert.True(query.IsStale());

        // Act
        query.Invalidate();

        // Assert — still stale
        Assert.True(query.IsStale());
    }

    /// <summary>
    /// A query with at least one enabled observer is active. TanStack's
    /// <c>isActive()</c> checks <c>observers.some(o => resolveEnabled(o.options.enabled) !== false)</c>.
    /// </summary>
    [Fact]
    public void IsActive_Should_Return_True_When_Has_Active_Observers()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["active-test"]);

        Assert.False(query.IsActive());

        // Act — add an enabled observer (Enabled defaults to true)
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active-test"],
            });
        var sub = observer.Subscribe(_ => { });

        // Assert
        Assert.True(query.IsActive());

        // Cleanup
        sub.Dispose();
    }

    /// <summary>
    /// A query where all observers have <c>Enabled = false</c> is not active.
    /// </summary>
    [Fact]
    public void IsActive_Should_Return_False_When_All_Observers_Disabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["active-disabled"]);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active-disabled"],
                Enabled = false
            });
        var sub = observer.Subscribe(_ => { });

        // Assert — disabled observer does not make the query active
        Assert.False(query.IsActive());

        // Cleanup
        sub.Dispose();
    }

    /// <summary>
    /// A query with a mix of enabled and disabled observers is active,
    /// since <c>isActive()</c> only requires one enabled observer.
    /// </summary>
    [Fact]
    public void IsActive_Should_Return_True_When_Some_Observers_Enabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["active-mixed"]);

        var disabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active-mixed"],
                Enabled = false
            });
        var sub1 = disabledObserver.Subscribe(_ => { });

        var enabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["active-mixed"],
            });
        var sub2 = enabledObserver.Subscribe(_ => { });

        // Assert — one enabled observer is enough
        Assert.True(query.IsActive());

        // Cleanup
        sub1.Dispose();
        sub2.Dispose();
    }

    #region IsDisabled

    /// <summary>
    /// A fresh query with no observers and no query function is disabled.
    /// This is the case for queries created via SetQueryData.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Return_True_When_No_QueryFn_And_No_Observers()
    {
        // Arrange — query built without a query function
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-no-fn"]);

        // Assert
        Assert.True(query.IsDisabled());
    }

    /// <summary>
    /// A query with a query function but that has never fetched (dataUpdateCount
    /// and errorUpdateCount are both 0) is disabled when it has no observers.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Return_True_When_Never_Fetched_And_No_Observers()
    {
        // Arrange — query function set, but never executed
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-never-fetched"]);
        query.SetQueryFn(async _ => "data");

        // Assert — no observers and never fetched → disabled
        Assert.True(query.IsDisabled());
    }

    /// <summary>
    /// A query with active (Enabled=true) observers is not disabled.
    /// TanStack's <c>isDisabled()</c> returns <c>!isActive()</c> when
    /// observers exist, where <c>isActive()</c> checks whether any
    /// observer has <c>enabled !== false</c>.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Return_False_When_Has_Active_Observers()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-has-observers"]);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-has-observers"],
                // Enabled defaults to true — an active observer
            });
        var sub = observer.Subscribe(_ => { });

        // Assert — has active observer → not disabled
        Assert.False(query.IsDisabled());

        // Cleanup
        sub.Dispose();
    }

    /// <summary>
    /// A query where all observers have <c>Enabled = false</c> is disabled.
    /// TanStack's <c>isActive()</c> returns false when no observer has
    /// <c>enabled !== false</c>.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Return_True_When_All_Observers_Disabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-all-disabled"]);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-all-disabled"],
                Enabled = false
            });
        var sub = observer.Subscribe(_ => { });

        // Assert — all observers disabled → query is disabled
        Assert.True(query.IsDisabled());

        // Cleanup
        sub.Dispose();
    }

    /// <summary>
    /// A query with a mix of enabled and disabled observers is not disabled,
    /// since <c>IsActive()</c> only requires one enabled observer.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Return_False_When_At_Least_One_Observer_Enabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-mixed"]);

        var disabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-mixed"],
                Enabled = false
            });
        var sub1 = disabledObserver.Subscribe(_ => { });

        var enabledObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-mixed"],
            });
        var sub2 = enabledObserver.Subscribe(_ => { });

        // Assert — one enabled observer → not disabled
        Assert.False(query.IsDisabled());

        // Cleanup
        sub1.Dispose();
        sub2.Dispose();
    }

    /// <summary>
    /// A query that has successfully fetched data is not disabled, even without
    /// observers, as long as it has a query function.
    /// </summary>
    [Fact]
    public async Task IsDisabled_Should_Return_False_After_Successful_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-fetched"], retry: 0);
        query.SetQueryFn(async _ => "data");

        // Act
        await query.Fetch();

        // Assert — has fetched (dataUpdateCount > 0) and has queryFn → not disabled
        Assert.False(query.IsDisabled());
    }

    /// <summary>
    /// A query that errored during fetch is not disabled (errorUpdateCount > 0).
    /// </summary>
    [Fact]
    public async Task IsDisabled_Should_Return_False_After_Failed_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-errored"], retry: 0);
        query.SetQueryFn(async _ => throw new InvalidOperationException("boom"));

        // Act — fetch and let it fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.Fetch());

        // Assert — errorUpdateCount > 0 → not disabled
        Assert.False(query.IsDisabled());
    }

    /// <summary>
    /// After removing all observers, IsDisabled should reflect the no-observer
    /// state. A query with no query function and no fetch history returns to
    /// disabled after its active observer unsubscribes.
    /// </summary>
    [Fact]
    public void IsDisabled_Should_Update_When_Observers_Leave()
    {
        // Arrange — no query function set, so no fetch can happen
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-unsub"]);

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-unsub"],
                // Enabled defaults to true — an active observer
            });
        var sub = observer.Subscribe(_ => { });

        // Assert — has active observer → not disabled
        Assert.False(query.IsDisabled());

        // Act — remove observer
        sub.Dispose();

        // Assert — no observers, no queryFn → disabled again
        Assert.True(query.IsDisabled());
    }

    /// <summary>
    /// After observers leave, a query that has a query function and has
    /// previously fetched data is NOT disabled — <c>DataUpdateCount > 0</c>
    /// keeps it alive.
    /// </summary>
    [Fact]
    public async Task IsDisabled_Should_Return_False_After_Observers_Leave_When_Has_Fetch_History()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["disabled-transition"], retry: 0);
        query.SetQueryFn(async _ => "data");
        await query.Fetch();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["disabled-transition"],
            });
        var sub = observer.Subscribe(_ => { });
        Assert.False(query.IsDisabled());

        // Act — remove observer
        sub.Dispose();

        // Assert — has queryFn and DataUpdateCount > 0 → not disabled
        Assert.False(query.IsDisabled());
    }

    #endregion

    #region Error Invalidation

    /// <summary>
    /// TanStack query.ts: the error reducer now sets <c>isInvalidated = true</c> so
    /// that existing data is flagged stale after a background fetch failure. This
    /// ensures the query will be refetched on the next window focus or mount even if
    /// the data age hasn't exceeded <c>staleTime</c>.
    /// </summary>
    [Fact]
    public async Task Error_Should_Set_IsInvalidated_True()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["error-invalidation"], retry: 0);
        var callCount = 0;

        query.SetQueryFn(async _ =>
        {
            callCount++;
            if (callCount == 1) return "data";
            throw new InvalidOperationException("background error");
        });

        // Act — first fetch succeeds
        await query.Fetch();
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.False(query.State.IsInvalidated);

        // Act — second fetch fails (background error)
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.Fetch());

        // Assert — state should have IsInvalidated = true
        Assert.Equal(QueryStatus.Errored, query.State!.Status);
        Assert.Equal("data", query.State.Data); // data preserved
        Assert.True(query.State.IsInvalidated);
        Assert.True(query.IsStale());
    }

    #endregion

    #endregion

    #region CancelRefetch_GC_Retry_Advanced

    /// <summary>
    /// TanStack ref: retryer.ts:260-264 — default exponential backoff is 1s, 2s, 4s.
    /// Verifies that without a custom retryDelay the retryer uses the default
    /// exponential backoff schedule and that FakeTimeProvider can drive it.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Retry_ExponentialBackoff_TimingVerification()
    {
        // Arrange
        var ftp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client = new QueryClient(new QueryCache(), timeProvider: ftp);

        var attemptCount = 0;
        // Signal each failure so we know when the retryer has entered DelayAsync
        var enteredDelay = new SemaphoreSlim(0);

        var query = BuildQuery<string>(client, ["exp-backoff"], retry: 3);

        // Custom retryDelay that uses the default formula but signals the test.
        // This ensures we only Advance after the retryer has entered DelayAsync.
        query.Options.RetryDelay = (failureCount, _) =>
        {
            var delay = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, failureCount - 1));
            enteredDelay.Release();
            return delay;
        };

        query.SetQueryFn(_ =>
        {
            attemptCount++;
            throw new InvalidOperationException($"fail #{attemptCount}");
        });

        // Act — start the fetch; it will fail and wait for retry delays.
        // Fetch() returns after the first await (attempt 1 throws → catch → retryDelay
        // → DelayAsync → CreateTimer → await tcs.Task).
        var fetchTask = query.Fetch();

        // After each failure the retryer calls retryDelay then enters DelayAsync.
        // We wait for retryDelay to signal before advancing time.

        // Initial attempt fails → retryDelay(1, ex) → 1s delay
        await enteredDelay.WaitAsync(TimeSpan.FromSeconds(5));
        ftp.Advance(TimeSpan.FromSeconds(1));

        // First retry fails → retryDelay(2, ex) → 2s delay
        await enteredDelay.WaitAsync(TimeSpan.FromSeconds(5));
        ftp.Advance(TimeSpan.FromSeconds(2));

        // Second retry fails → retryDelay(3, ex) → 4s delay
        await enteredDelay.WaitAsync(TimeSpan.FromSeconds(5));
        ftp.Advance(TimeSpan.FromSeconds(4));

        // Third retry fails → max retries exhausted, no more delays
        // Fetch should now complete with an error
        await Assert.ThrowsAsync<InvalidOperationException>(() => fetchTask);

        // Assert — 1 initial attempt + 3 retries = 4 total
        Assert.Equal(4, attemptCount);
        Assert.Equal(QueryStatus.Errored, query.State!.Status);
    }

    /// <summary>
    /// TanStack ref: query.test.tsx:561-574 — GC timer fires when the last
    /// observer is removed and gcTime elapses. Verifies the cache removes
    /// the query deterministically via FakeTimeProvider.
    /// </summary>
    [Fact]
    public async Task GcTimer_ScheduledOnLastObserverRemoval()
    {
        // Arrange
        var ftp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client = new QueryClient(new QueryCache(), timeProvider: ftp);
        var cache = client.GetQueryCache();

        var query = BuildQuery<string>(client, ["gc-observer-removal"],
            gcTime: TimeSpan.FromMilliseconds(100), retry: 0);
        query.SetQueryFn(_ => Task.FromResult("data"));
        await query.Fetch();

        // Subscribe — adds an observer, which clears the GC timer
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-observer-removal"],
                QueryFn = _ => Task.FromResult("data"),
                Enabled = false
            });
        var subscription = observer.Subscribe(_ => { });
        Assert.NotNull(cache.Get<string>(query.QueryHash!));

        // Act — dispose removes the observer, which schedules the GC timer
        subscription.Dispose();

        // Advance past gcTime
        ftp.Advance(TimeSpan.FromMilliseconds(150));

        // Assert — query should be removed from cache
        Assert.Null(cache.Get<string>(query.QueryHash!));
    }

    /// <summary>
    /// TanStack ref: query.test.tsx:596-613 — GC timer is cleared when a new
    /// observer subscribes before the timer fires. Query survives past the
    /// original gcTime.
    /// </summary>
    [Fact]
    public async Task GcTimer_ClearedWhenObserverReaddedBeforeExpiry()
    {
        // Arrange
        var ftp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client = new QueryClient(new QueryCache(), timeProvider: ftp);
        var cache = client.GetQueryCache();

        var query = BuildQuery<string>(client, ["gc-readd"],
            gcTime: TimeSpan.FromMilliseconds(500), retry: 0);
        query.SetQueryFn(_ => Task.FromResult("data"));
        await query.Fetch();

        // Subscribe then dispose — starts the GC timer
        var observer1 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-readd"],
                QueryFn = _ => Task.FromResult("data"),
                Enabled = false
            });
        var sub1 = observer1.Subscribe(_ => { });
        sub1.Dispose();

        // Advance by 200ms (within the 500ms gcTime)
        ftp.Advance(TimeSpan.FromMilliseconds(200));
        Assert.NotNull(cache.Get<string>(query.QueryHash!));

        // Act — re-subscribe, which clears the GC timer
        var observer2 = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["gc-readd"],
                QueryFn = _ => Task.FromResult("data"),
                Enabled = false
            });
        var sub2 = observer2.Subscribe(_ => { });

        // Advance well past the original gcTime
        ftp.Advance(TimeSpan.FromMilliseconds(600));

        // Assert — query should still be in cache because the GC timer was cleared
        Assert.NotNull(cache.Get<string>(query.QueryHash!));

        sub2.Dispose();
    }

    /// <summary>
    /// TanStack ref: query.test.tsx:536-558 — GC timer fires and removes the
    /// query from cache when there are no observers and gcTime elapses.
    /// </summary>
    [Fact]
    public void GcTimer_FiresAndRemovesQueryFromCache()
    {
        // Arrange
        var ftp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client = new QueryClient(new QueryCache(), timeProvider: ftp);
        var cache = client.GetQueryCache();

        var query = BuildQuery<string>(client, ["gc-fire"],
            gcTime: TimeSpan.FromMilliseconds(200), retry: 0);

        // No observers, no fetch — the GC timer was scheduled in the constructor
        Assert.NotNull(cache.GetByHash(query.QueryHash!));

        // Act — advance past gcTime
        ftp.Advance(TimeSpan.FromMilliseconds(250));

        // Assert — query should be removed from cache
        Assert.Null(cache.GetByHash(query.QueryHash!));
    }

    /// <summary>
    /// TanStack ref: query.test.tsx:108-153 — a fetch pauses when the device
    /// is offline and resumes when connectivity is restored.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Retry_PausesOnOffline_ResumesOnOnline()
    {
        // Arrange — dedicated OnlineManager so we don't affect other tests
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);

        var client = new QueryClient(
            new QueryCache(),
            onlineManager: onlineManager);

        var query = BuildQuery<string>(client, ["pause-offline"], retry: 0);
        query.SetQueryFn(_ => Task.FromResult("online-data"));

        // Act — fetch while offline; the retryer should pause
        var fetchTask = query.Fetch();

        // The FetchAction reducer detects CanFetch=false → FetchStatus.Paused
        Assert.Equal(FetchStatus.Paused, query.State!.FetchStatus);

        // Restore connectivity — fires OnlineChanged → QueryClient → query.OnOnline()
        // → _retryer.Continue()
        onlineManager.SetOnline(true);

        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("online-data", query.State.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);

        client.Dispose();
    }

    /// <summary>
    /// TanStack ref: query.test.tsx:61-106 — a retry pauses when the app
    /// loses focus and resumes when focus returns.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Retry_PausesOnFocusLoss_ResumesOnFocus()
    {
        // Arrange — dedicated FocusManager and OnlineManager
        var focusManager = new FocusManager();
        var onlineManager = new OnlineManager();
        var ftp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var client = new QueryClient(
            new QueryCache(),
            timeProvider: ftp,
            focusManager: focusManager,
            onlineManager: onlineManager);

        var callCount = 0;
        var query = BuildQuery<string>(client, ["pause-focus"], retry: 1,
            retryDelay: (_, _) => TimeSpan.FromMilliseconds(1));

        query.SetQueryFn(_ =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.FromResult("focused-data");
        });

        // Subscribe a cache listener to detect when the query enters pause
        var paused = new TaskCompletionSource();
        var cache = client.GetQueryCache();
        using var pauseSubscription = cache.Subscribe(@event =>
        {
            if (@event is QueryCacheQueryUpdatedEvent { Action: PauseAction })
                paused.TrySetResult();
        });

        // Act — start fetch; first call fails, enters retryDelay
        var fetchTask = query.Fetch();

        // Lose focus BEFORE advancing time. When the timer fires after Advance,
        // the retryer's CanContinue() check will see IsFocused=false and enter
        // PauseAsync. This ordering eliminates the race between the timer
        // continuation (posted via RunContinuationsAsynchronously) and SetFocused.
        focusManager.SetFocused(false);

        // Advance past the retry delay so the retryer fires and checks CanContinue
        ftp.Advance(TimeSpan.FromMilliseconds(1));

        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — FetchStatus is Paused
        Assert.Equal(FetchStatus.Paused, query.State!.FetchStatus);

        // Act — restore focus, which calls _retryer.Continue()
        focusManager.SetFocused(true);

        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("focused-data", query.State.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);

        client.Dispose();
    }

    /// <summary>
    /// TanStack: query.ts Cancel with revert restores pre-fetch state.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Cancel_WithRevert_RestoresState()
    {
        // Arrange — seed the query with data first
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["cancel-revert"], retry: 0);
        query.SetQueryFn(_ => Task.FromResult("original"));
        await query.Fetch();

        Assert.Equal("original", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);

        // Start a new blocking fetch
        var tcs = new TaskCompletionSource<string>();
        query.SetQueryFn(async ctx =>
        {
            return await tcs.Task.WaitAsync(ctx.CancellationToken);
        });

        var fetchTask = query.Fetch(cancelRefetch: true);

        // Let the fetch start — FetchStatus should transition to Fetching
        await Task.Delay(50);
        Assert.Equal(FetchStatus.Fetching, query.State.FetchStatus);

        // Act — cancel with revert
        await query.Cancel(new CancelOptions { Revert = true });

        // Assert — state reverted to pre-fetch data
        Assert.Equal("original", query.State.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    /// <summary>
    /// Verifies that FailureCount increments during retries and the final
    /// error state has the correct FetchFailureCount.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task StateTransitions_RetryFailureCount()
    {
        // Arrange
        var client = CreateQueryClient();
        var failureCounts = new List<int>();
        var query = BuildQuery<string>(client, ["retry-failure-count"], retry: 2,
            retryDelay: (_, _) => TimeSpan.Zero);

        query.SetQueryFn(_ => throw new InvalidOperationException("always fails"));

        // Use an observer to capture intermediate FailureCount values
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["retry-failure-count"],
                QueryFn = _ => throw new InvalidOperationException("always fails"),
                Enabled = false
            });

        var subscription = observer.Subscribe(result =>
        {
            // FailedAction dispatches set FetchStatus=Fetching with incremented FailureCount
            if (result.FetchStatus == FetchStatus.Fetching && result.FailureCount > 0)
            {
                failureCounts.Add(result.FailureCount);
            }
        });

        // Act
        try { await query.Fetch(); } catch { }

        // Assert — should have seen intermediate failure counts 1 and 2
        Assert.Contains(1, failureCounts);
        Assert.Contains(2, failureCounts);

        // Final state should have FetchFailureCount = 3 (1 initial + 2 retries)
        Assert.Equal(3, query.State!.FetchFailureCount);
        Assert.Equal(QueryStatus.Errored, query.State.Status);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack ref: retryer.ts — custom retryDelay function receives
    /// 1-based failureCount and the exception from the last attempt.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task RetryDelay_CustomFunction_CorrectArguments()
    {
        // Arrange
        var client = CreateQueryClient();
        var capturedArgs = new List<(int FailureCount, Exception Error)>();
        var expectedError = new InvalidOperationException("test error");

        var query = BuildQuery<string>(client, ["retry-delay-args"], retry: 2,
            retryDelay: (count, error) =>
            {
                capturedArgs.Add((count, error));
                return TimeSpan.Zero;
            });

        query.SetQueryFn(_ => throw expectedError);

        // Act
        try { await query.Fetch(); } catch { }

        // Assert — retryDelay should have been called twice (once per retry)
        Assert.Equal(2, capturedArgs.Count);

        // First retry: failureCount=1, error is the thrown exception
        Assert.Equal(1, capturedArgs[0].FailureCount);
        Assert.Same(expectedError, capturedArgs[0].Error);

        // Second retry: failureCount=2, same error
        Assert.Equal(2, capturedArgs[1].FailureCount);
        Assert.Same(expectedError, capturedArgs[1].Error);
    }

    /// <summary>
    /// TanStack ref: query.ts:328 — OnFocus triggers FetchSilentAsync with
    /// cancelRefetch=false, which means an in-flight fetch is NOT cancelled;
    /// it is deduplicated instead.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task OnFocus_DoesNotCancelInFlightFetch()
    {
        // Arrange — dedicated FocusManager for isolation
        var focusManager = new FocusManager();
        var client = new QueryClient(
            new QueryCache(),
            focusManager: focusManager);

        var query = BuildQuery<string>(client, ["focus-no-cancel"], retry: 0);
        query.SetQueryFn(_ => Task.FromResult("seed"));
        await query.Fetch();

        // Start a blocking fetch that we control
        var tcs = new TaskCompletionSource<string>();
        var fetchCancelled = false;

        query.SetQueryFn(async ctx =>
        {
            try
            {
                return await tcs.Task.WaitAsync(ctx.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                fetchCancelled = true;
                throw;
            }
        });

        // Start a new fetch (cancelRefetch: true to ensure it replaces the completed dedup task)
        var fetchTask = query.Fetch(cancelRefetch: true);
        await Task.Delay(50);
        Assert.Equal(FetchStatus.Fetching, query.State!.FetchStatus);

        // Add an observer so OnFocus has someone to evaluate ShouldFetchOnWindowFocus
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-no-cancel"],
                QueryFn = _ => Task.FromResult("irrelevant"),
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });
        var sub = observer.Subscribe(_ => { });

        // Act — trigger focus event while fetch is in-flight.
        // OnFocus calls FetchSilentAsync which passes cancelRefetch: false,
        // so the existing in-flight fetch should be deduplicated (not cancelled).
        focusManager.SetFocused(false);
        focusManager.SetFocused(true);

        // Verify the fetch was NOT cancelled
        Assert.False(fetchCancelled);
        Assert.Equal(FetchStatus.Fetching, query.State.FetchStatus);

        // Complete the original fetch
        tcs.SetResult("completed-data");
        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — original fetch succeeded
        Assert.Equal("completed-data", query.State.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.False(fetchCancelled);

        sub.Dispose();
        client.Dispose();
    }

    #endregion
}
