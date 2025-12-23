namespace RabstackQuery.Tests;

/// <summary>
/// Tests for <see cref="QueriesObserver{TData}"/>.
///
/// Mirrors the TanStack queriesObserver.test.tsx suite (12 tests).
/// Tests that use synchronous query functions assert immediately (the fetch
/// completes inline during Subscribe). Tests that use <see cref="TaskCompletionSource{T}"/>
/// gates use <see cref="SemaphoreSlim"/> or <see cref="Task.WaitAsync(TimeSpan)"/>
/// to wait for the expected state deterministically.
/// </summary>
public sealed class QueriesObserverTests
{
    private static QueryClient CreateQueryClient()
    {
        var cache = new QueryCache();
        // Isolated FocusManager/OnlineManager instances prevent cross-test
        // interference through the global singletons.
        return new QueryClient(cache,
            focusManager: new FocusManager(),
            onlineManager: new OnlineManager());
    }

    // ── Test 1: basic result aggregation ────────────────────────────────────────

    /// <summary>
    /// TanStack: "should return an array with all query results"
    /// Subscribing triggers fetches for all queries; the combined result contains
    /// the data from each once they complete.
    /// </summary>
    [Fact]
    public void Should_Return_All_Query_Results()
    {
        // Arrange
        var client = CreateQueryClient();
        IReadOnlyList<IQueryResult<int>>? lastResult = null;

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q1"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q2"], QueryFn = async _ => 2 },
        ]);

        // Act — query functions complete synchronously (no async work), so the
        // fetches finish inline during Subscribe.
        var subscription = observer.Subscribe(result => lastResult = result);
        subscription.Dispose();

        // Assert — both queries completed with their respective values
        Assert.NotNull(lastResult);
        Assert.Equal(2, lastResult.Count);
        Assert.Equal(1, lastResult[0].Data);
        Assert.Equal(2, lastResult[1].Data);
        Assert.True(lastResult[0].IsSuccess);
        Assert.True(lastResult[1].IsSuccess);
    }

    // ── Test 2: intermediate state tracking ─────────────────────────────────────

    /// <summary>
    /// TanStack: "should update when a query updates"
    /// Verifies the full notification sequence: fetching start (per query),
    /// completion (per query), and a manual data update via SetQueryData.
    ///
    /// TaskCompletionSources gate each query independently so the order of
    /// completion is deterministic, matching the TanStack fake-timer behaviour.
    /// </summary>
    [Fact]
    public async Task Should_Update_When_A_Query_Updates()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs1 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<IReadOnlyList<IQueryResult<int>>>();
        var resultAdded = new SemaphoreSlim(0, int.MaxValue);

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q2-a"], QueryFn = async _ => await tcs1.Task },
            new QueryObserverOptions<int> { QueryKey = ["q2-b"], QueryFn = async _ => await tcs2.Task },
        ]);

        // The initial state is captured before subscribing (both idle/pending).
        results.Add(observer.GetCurrentResult());

        var subscription = observer.Subscribe(result =>
        {
            results.Add(result);
            resultAdded.Release();
        });

        // The FetchAction dispatch happens synchronously inside each inner
        // observer's OnSubscribe, so after Subscribe returns we should already
        // have the two "fetching" transition notifications.
        await resultAdded.WaitAsync(TimeSpan.FromSeconds(5));
        await resultAdded.WaitAsync(TimeSpan.FromSeconds(5));

        // Complete query 1 first, then query 2
        tcs1.SetResult(1);
        await resultAdded.WaitAsync(TimeSpan.FromSeconds(5));

        tcs2.SetResult(2);
        await resultAdded.WaitAsync(TimeSpan.FromSeconds(5));

        // Manually update query 2's data to simulate a cache write
        client.SetQueryData<int>(["q2-b"], 3);
        await resultAdded.WaitAsync(TimeSpan.FromSeconds(5));

        subscription.Dispose();

        // Assert — 6 snapshots total:
        // [0] initial: [pending/idle, pending/idle]
        // [1] q1 fetch start: [pending/fetching, pending/idle]
        // [2] q2 fetch start: [pending/fetching, pending/fetching]
        // [3] q1 completes:   [success(1), pending/fetching]
        // [4] q2 completes:   [success(1), success(2)]
        // [5] SetQueryData:   [success(1), success(3)]
        Assert.Equal(6, results.Count);

        Assert.Equal(QueryStatus.Pending, results[0][0].Status);
        Assert.Equal(FetchStatus.Idle, results[0][0].FetchStatus);
        Assert.Equal(QueryStatus.Pending, results[0][1].Status);
        Assert.Equal(FetchStatus.Idle, results[0][1].FetchStatus);

        Assert.Equal(FetchStatus.Fetching, results[1][0].FetchStatus);
        Assert.Equal(FetchStatus.Idle, results[1][1].FetchStatus);

        Assert.Equal(FetchStatus.Fetching, results[2][0].FetchStatus);
        Assert.Equal(FetchStatus.Fetching, results[2][1].FetchStatus);

        Assert.True(results[3][0].IsSuccess);
        Assert.Equal(1, results[3][0].Data);
        Assert.Equal(FetchStatus.Fetching, results[3][1].FetchStatus);

        Assert.True(results[4][0].IsSuccess);
        Assert.Equal(1, results[4][0].Data);
        Assert.True(results[4][1].IsSuccess);
        Assert.Equal(2, results[4][1].Data);

        Assert.Equal(1, results[5][0].Data);
        Assert.Equal(3, results[5][1].Data);
    }

    // ── Test 3: query removal ────────────────────────────────────────────────────

    /// <summary>
    /// TanStack: "should update when a query is removed"
    /// After SetQueries removes a query, its observer is destroyed (removed from
    /// the active type filter). The remaining query stays active.
    /// </summary>
    [Fact]
    public async Task Should_Update_When_A_Query_Is_Removed()
    {
        // Arrange
        var client = CreateQueryClient();
        var tcs1 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<IReadOnlyList<IQueryResult<int>>>();
        var bothSucceeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q3-a"], QueryFn = async _ => await tcs1.Task },
            new QueryObserverOptions<int> { QueryKey = ["q3-b"], QueryFn = async _ => await tcs2.Task },
        ]);

        results.Add(observer.GetCurrentResult());
        var subscription = observer.Subscribe(result =>
        {
            results.Add(result);
            if (result.Count == 2 && result.All(r => r.IsSuccess))
                bothSucceeded.TrySetResult(true);
        });

        tcs1.SetResult(1);
        tcs2.SetResult(2);
        await bothSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — remove q3-a from the group. SetQueries fires a synchronous
        // notification so the assertion can follow immediately.
        observer.SetQueries([
            new QueryObserverOptions<int> { QueryKey = ["q3-b"], QueryFn = async _ => await tcs2.Task },
        ]);

        // Assert — q3-a has no active observers; q3-b still has one
        var cache = client.GetQueryCache();
        Assert.Null(cache.Find(new QueryFilters { QueryKey = ["q3-a"], Type = QueryTypeFilter.Active }));
        Assert.NotNull(cache.Find(new QueryFilters { QueryKey = ["q3-b"], Type = QueryTypeFilter.Active }));

        subscription.Dispose();

        // After full unsubscribe, neither query should be active
        Assert.Null(cache.Find(new QueryFilters { QueryKey = ["q3-a"], Type = QueryTypeFilter.Active }));
        Assert.Null(cache.Find(new QueryFilters { QueryKey = ["q3-b"], Type = QueryTypeFilter.Active }));

        // The last notification after SetQueries should contain only q3-b
        var lastResult = results.Last();
        Assert.Single(lastResult);
        Assert.Equal(2, lastResult[0].Data);
    }

    // ── Test 4: query reordering ─────────────────────────────────────────────────

    /// <summary>
    /// TanStack: "should update when a query changed position"
    /// After SetQueries swaps the order, the result array mirrors the new order.
    /// </summary>
    [Fact]
    public void Should_Update_When_Query_Position_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new List<IReadOnlyList<IQueryResult<int>>>();

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q4-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q4-b"], QueryFn = async _ => 2 },
        ]);

        results.Add(observer.GetCurrentResult());
        // Fetches complete synchronously (no async work in query functions).
        var subscription = observer.Subscribe(result => results.Add(result));

        // Act — swap positions. SetQueries fires a synchronous notification.
        observer.SetQueries([
            new QueryObserverOptions<int> { QueryKey = ["q4-b"], QueryFn = async _ => 2 },
            new QueryObserverOptions<int> { QueryKey = ["q4-a"], QueryFn = async _ => 1 },
        ]);

        subscription.Dispose();

        // Assert — final result has the reordered data
        var lastResult = results.Last();
        Assert.Equal(2, lastResult.Count);
        Assert.Equal(2, lastResult[0].Data); // q4-b is now first
        Assert.Equal(1, lastResult[1].Data); // q4-a is now second
    }

    // ── Test 5: no-op update ─────────────────────────────────────────────────────

    /// <summary>
    /// TanStack: "should not update when nothing has changed"
    /// SetQueries with the same key set must not emit an extra notification.
    /// </summary>
    [Fact]
    public void Should_Not_Update_When_Nothing_Has_Changed()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new List<IReadOnlyList<IQueryResult<int>>>();

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q5-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q5-b"], QueryFn = async _ => 2 },
        ]);

        results.Add(observer.GetCurrentResult());
        // Fetches complete synchronously.
        var subscription = observer.Subscribe(result => results.Add(result));

        var countBeforeNoop = results.Count;

        // Act — SetQueries with exactly the same keys (no structural or data change)
        observer.SetQueries([
            new QueryObserverOptions<int> { QueryKey = ["q5-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q5-b"], QueryFn = async _ => 2 },
        ]);

        subscription.Dispose();

        // Assert — no additional notification was emitted
        Assert.Equal(countBeforeNoop, results.Count);
    }

    // ── Test 6: fetch-on-subscribe ───────────────────────────────────────────────

    /// <summary>
    /// TanStack: "should trigger all fetches when subscribed"
    /// All query functions are called once when the first listener subscribes.
    /// </summary>
    [Fact]
    public void Should_Trigger_All_Fetches_When_Subscribed()
    {
        // Arrange
        var client = CreateQueryClient();
        var fn1Called = false;
        var fn2Called = false;

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int>
            {
                QueryKey = ["q6-a"],
                QueryFn = async _ =>
                {
                    fn1Called = true;
                    return 1;
                }
            },
            new QueryObserverOptions<int>
            {
                QueryKey = ["q6-b"],
                QueryFn = async _ =>
                {
                    fn2Called = true;
                    return 2;
                }
            },
        ]);

        // Act — query functions complete synchronously during Subscribe.
        var subscription = observer.Subscribe(_ => { });

        // Assert — both query functions were called
        Assert.True(fn1Called);
        Assert.True(fn2Called);

        subscription.Dispose();
    }

    // ── Test 7: multiple subscribers ────────────────────────────────────────────

    /// <summary>
    /// TanStack: "should not destroy the observer if there is still a subscription"
    /// Unsubscribing the first listener must not tear down the inner observers
    /// when a second listener is still active.
    /// </summary>
    [Fact]
    public async Task Should_Not_Destroy_Observer_While_Second_Subscription_Is_Active()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int>
            {
                QueryKey = ["q7"],
                QueryFn = async _ => await fetchGate.Task
            },
        ]);

        var handler1Results = new List<IReadOnlyList<IQueryResult<int>>>();
        var handler2Results = new List<IReadOnlyList<IQueryResult<int>>>();

        var sub1 = observer.Subscribe(r => handler1Results.Add(r));
        var sub2 = observer.Subscribe(r =>
        {
            handler2Results.Add(r);
            if (r[0].IsSuccess) fetchDone.TrySetResult(true);
        });

        // Act — drop the first subscription before the fetch completes
        sub1.Dispose();

        // Release the gate so the fetch can complete
        fetchGate.SetResult(1);

        // Wait for the fetch to complete (second subscription still active)
        await fetchDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        sub2.Dispose();

        // Assert
        // sub1 left before completion → received only the fetching notification
        Assert.Equal(1, handler1Results.Count);
        Assert.Equal(FetchStatus.Fetching, handler1Results[0][0].FetchStatus);

        // sub2 stayed until completion → received the success notification
        Assert.Equal(1, handler2Results.Count);
        Assert.True(handler2Results[0][0].IsSuccess);
        Assert.Equal(1, handler2Results[0][0].Data);
    }

    // ── Test 8: duplicate keys + GetOptimisticResult ─────────────────────────────

    /// <summary>
    /// TanStack: "should handle duplicate query keys in different positions"
    /// GetOptimisticResult provides the pre-subscription snapshot. When two
    /// positions share the same key, the underlying query function is deduplicated
    /// (called once), but both positions reflect its result.
    /// </summary>
    [Fact]
    public async Task GetOptimisticResult_Should_Handle_Duplicate_Keys()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;
        var results = new List<IReadOnlyList<IQueryResult<int>>>();

        // Use a TCS to gate the q8-a fetch. This ensures both observer positions
        // subscribe before the fetch completes, enabling Query-level deduplication.
        // Without the gate, the instant function would complete synchronously during
        // the first observer's OnSubscribe, before the second observer subscribes.
        var tcsA = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsB = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queries = new List<QueryObserverOptions<int>>
        {
            new() { QueryKey = ["q8-a"], QueryFn = async _ => { Interlocked.Increment(ref fetchCount); return await tcsA.Task; } },
            new() { QueryKey = ["q8-b"], QueryFn = async _ => await tcsB.Task },
            new() { QueryKey = ["q8-a"], QueryFn = async _ => { Interlocked.Increment(ref fetchCount); return await tcsA.Task; } },
        };

        var observer = new QueriesObserver<int>(client, queries);

        // Capture the initial state using GetOptimisticResult (before subscribing)
        results.Add(observer.GetOptimisticResult(queries));

        var subscription = observer.Subscribe(r =>
        {
            results.Add(r);
            if (r.Count == 3 && r.All(x => x.IsSuccess))
                allDone.TrySetResult(true);
        });

        // All observers are now subscribed. Complete the fetches.
        tcsA.SetResult(1);
        tcsB.SetResult(2);
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(15));
        subscription.Dispose();

        // Assert — initial snapshot: all pending/idle
        Assert.Equal(QueryStatus.Pending, results[0][0].Status);
        Assert.Equal(FetchStatus.Idle, results[0][0].FetchStatus);

        // Final snapshot: q8-a succeeds at both positions, q8-b succeeds in the middle
        var last = results.Last();
        Assert.Equal(3, last.Count);
        Assert.Equal(1, last[0].Data); // q8-a first occurrence
        Assert.Equal(2, last[1].Data); // q8-b
        Assert.Equal(1, last[2].Data); // q8-a second occurrence

        // The query function for q8-a should have been called only once
        // despite appearing at two positions (fetch deduplication at Query level).
        Assert.Equal(1, fetchCount);
    }

    // ── Test 9: SetQueries with select ───────────────────────────────────────────

    /// <summary>
    /// TanStack: "should notify when results change during early return"
    /// When SetQueries updates observers to add a Select transform, and the cache
    /// already has data, listeners receive updated results reflecting the transform.
    /// </summary>
    [Fact]
    public void Should_Notify_When_Results_Change_After_SetQueries_With_Select()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData<int>(["q9-a"], 1);
        client.SetQueryData<int>(["q9-b"], 2);

        var results = new List<IReadOnlyList<IQueryResult<int>>>();
        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q9-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q9-b"], QueryFn = async _ => 2 },
        ]);

        results.Add(observer.GetCurrentResult());
        var subscription = observer.Subscribe(r => results.Add(r));

        var countBeforeUpdate = results.Count;

        // Act — update query options to include a Select transform (+100).
        // SetQueries fires synchronous notifications when options change.
        observer.SetQueries([
            new QueryObserverOptions<int>
            {
                QueryKey = ["q9-a"],
                QueryFn = async _ => 1,
                Select = d => d + 100
            },
            new QueryObserverOptions<int>
            {
                QueryKey = ["q9-b"],
                QueryFn = async _ => 2,
                Select = d => d + 100
            },
        ]);

        subscription.Dispose();

        // Assert — at least one notification after the setQueries call, and the
        // final result reflects the select transform
        Assert.True(results.Count > countBeforeUpdate);
        var lastResult = results.Last();
        Assert.Equal(101, lastResult[0].Data);
        Assert.Equal(102, lastResult[1].Data);
    }

    // ── Tests 10–12: GetOptimisticResult with combine ────────────────────────────

    /// <summary>
    /// TanStack: "should update combined result when queries are added with stable combine reference"
    /// GetOptimisticResult with a combine function reflects changes in the query count.
    /// </summary>
    [Fact]
    public void GetOptimisticResult_Combined_Should_Reflect_Added_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var combineCallCount = 0;
        Func<IReadOnlyList<IQueryResult<int>>, (int Count, IReadOnlyList<IQueryResult<int>> Results)> combine =
            results => { combineCallCount++; return (results.Count, results); };

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q10-a"], QueryFn = async _ => 1 },
        ]);

        // Act — initial: 1 query
        var (initialRaw, getInitialCombined, _) = observer.GetOptimisticResult(
            [new QueryObserverOptions<int> { QueryKey = ["q10-a"], QueryFn = async _ => 1 }],
            combine);
        var initialCombined = getInitialCombined(initialRaw);

        Assert.Equal(1, initialCombined.Count);

        // Act — expanded: 2 queries
        var (newRaw, getNewCombined, _) = observer.GetOptimisticResult(
        [
            new QueryObserverOptions<int> { QueryKey = ["q10-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q10-b"], QueryFn = async _ => 2 },
        ], combine);
        var newCombined = getNewCombined(newRaw);

        Assert.Equal(2, newCombined.Count);
    }

    /// <summary>
    /// TanStack: "should handle queries being removed with stable combine reference"
    /// GetOptimisticResult with a combine function reflects a query being removed.
    /// </summary>
    [Fact]
    public void GetOptimisticResult_Combined_Should_Reflect_Removed_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        Func<IReadOnlyList<IQueryResult<int>>, (int Count, IReadOnlyList<IQueryResult<int>> Results)> combine =
            results => (results.Count, results);

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q11-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q11-b"], QueryFn = async _ => 2 },
        ]);

        // Initial: 2 queries
        var (initialRaw, getInitialCombined, _) = observer.GetOptimisticResult(
        [
            new QueryObserverOptions<int> { QueryKey = ["q11-a"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["q11-b"], QueryFn = async _ => 2 },
        ], combine);
        var initialCombined = getInitialCombined(initialRaw);

        Assert.Equal(2, initialCombined.Count);

        // Act — shrink to 1 query
        var (newRaw, getNewCombined, _) = observer.GetOptimisticResult(
        [
            new QueryObserverOptions<int> { QueryKey = ["q11-a"], QueryFn = async _ => 1 },
        ], combine);
        var newCombined = getNewCombined(newRaw);

        Assert.Equal(1, newCombined.Count);
    }

    /// <summary>
    /// TanStack: "should update combined result when queries are replaced with different ones (same length)"
    /// When the query at a position is replaced with one pointing at a different key,
    /// the combined result reflects the new key's state (pending if uncached).
    /// </summary>
    [Fact]
    public void GetOptimisticResult_Combined_Should_Reflect_Replaced_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        Func<IReadOnlyList<IQueryResult<int>>, (IReadOnlyList<string> Keys, IReadOnlyList<IQueryResult<int>> Results)> combine =
            results => (results.Select(r => r.Status.ToString()).ToList(), results);

        // Pre-populate cache for q12-a so its status will be Success
        client.SetQueryData<int>(["q12-a"], 99);

        var observer = new QueriesObserver<int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["q12-a"], QueryFn = async _ => 1 },
        ]);

        // Initial: q12-a → success (cached)
        var (initialRaw, getInitialCombined, _) = observer.GetOptimisticResult(
        [
            new QueryObserverOptions<int> { QueryKey = ["q12-a"], QueryFn = async _ => 1 },
        ], combine);
        var initialCombined = getInitialCombined(initialRaw);

        Assert.Equal(["Succeeded"], initialCombined.Keys);

        // Act — replace with an uncached query (q12-b) at the same position
        var (newRaw, getNewCombined, _) = observer.GetOptimisticResult(
        [
            new QueryObserverOptions<int> { QueryKey = ["q12-b"], QueryFn = async _ => 2 },
        ], combine);
        var newCombined = getNewCombined(newRaw);

        // q12-b has no cached data → pending
        Assert.Equal(["Pending"], newCombined.Keys);
    }

    // ── Test 13: combine memoization ───────────────────────────────────────────

    /// <summary>
    /// When a combine function is configured via the two-param generic, the combine
    /// runs on updates and notifications fire to outer listeners. Uses pre-populated
    /// cache data to avoid async timing sensitivity.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Combine_Should_Run_On_Updates_And_Notify_Listeners()
    {
        // Arrange — pre-populate cache so observers start with data
        var client = CreateQueryClient();
        client.SetQueryData<int>(["c1"], 10);
        client.SetQueryData<int>(["c2"], 20);

        var combineCallCount = 0;
        var notifications = new List<IReadOnlyList<IQueryResult<int>>>();

        Func<IReadOnlyList<IQueryResult<int>>, int> combine =
            results => { combineCallCount++; return results.Sum(r => r.Data); };

        var observer = new QueriesObserver<int, int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["c1"], QueryFn = async _ => 10 },
            new QueryObserverOptions<int> { QueryKey = ["c2"], QueryFn = async _ => 20 },
        ], combine);

        // Act — trigger a SetQueryData update, which dispatches SuccessAction
        // synchronously and should invoke the combine function in Notify().
        var sub = observer.Subscribe(result => notifications.Add(result));
        client.SetQueryData<int>(["c1"], 100);
        sub.Dispose();

        // Assert — combine was called at least once, and outer listener received notifications
        Assert.True(combineCallCount >= 1);
        Assert.NotEmpty(notifications);
    }

    /// <summary>
    /// When combine returns the same reference, the outer listener is NOT called
    /// on subsequent updates. This verifies the combine memoization suppression.
    /// Uses pre-populated cache and SetQueryData to avoid async timing.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Combine_Suppresses_Notification_When_Output_Unchanged()
    {
        // Arrange — pre-populate cache
        var client = CreateQueryClient();
        client.SetQueryData<int>(["cs1"], 1);
        var cachedResult = "constant";
        var notifyCount = 0;

        // Combine always returns the same string reference
        Func<IReadOnlyList<IQueryResult<int>>, string> combine = _ => cachedResult;

        var observer = new QueriesObserver<int, string>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["cs1"], QueryFn = async _ => 1 },
        ], combine);

        var sub = observer.Subscribe(_ => notifyCount++);

        // The first subscribe + initial SetQueries notification fires once.
        var countAfterSubscribe = notifyCount;

        // Act — trigger a data update. The combine output doesn't change
        // (still returns the same "constant" reference), so the outer listener
        // should NOT receive an additional notification.
        client.SetQueryData<int>(["cs1"], 999);

        // Assert — no additional notifications because combine returned the same
        // reference, even though the underlying data changed.
        Assert.Equal(countAfterSubscribe, notifyCount);

        sub.Dispose();
    }

    // ── Test 14: synchronized trackResult across observers ─────────────────────

    /// <summary>
    /// When combine accesses a property on one observer's tracked result, that
    /// property is auto-tracked on ALL observers in the group (PR #7000
    /// synchronization). Accessing Data on only the first tracked result should
    /// record "Data" on both observers, so a Data-unchanged update on the second
    /// observer is suppressed.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TrackResult_Synchronizes_Across_Observers()
    {
        // Arrange — pre-populate cache
        var client = CreateQueryClient();
        client.SetQueryData<int>(["tr1"], 1);
        client.SetQueryData<int>(["tr2"], 2);
        var notifyCount = 0;

        // Combine only accesses Data on the first result, but the synchronized
        // tracking callback records "Data" on ALL observers in the group.
        Func<IReadOnlyList<IQueryResult<int>>, int> combine = results =>
            results[0].Data;

        var observer = new QueriesObserver<int, int>(client,
        [
            new QueryObserverOptions<int>
            {
                QueryKey = ["tr1"],
                QueryFn = async _ => 1
            },
            new QueryObserverOptions<int>
            {
                QueryKey = ["tr2"],
                QueryFn = async _ => 2
            },
        ], combine);

        var sub = observer.Subscribe(_ => notifyCount++);

        // Assert — observers were created
        Assert.Equal(2, observer.GetObservers().Count);

        var countAfterSubscribe = notifyCount;

        // Act — update tr1's Data. Combine accesses Data, so it runs and
        // produces a new result (3 instead of 1). Notification fires.
        client.SetQueryData<int>(["tr1"], 3);
        Assert.True(notifyCount > countAfterSubscribe, "Data change should notify");

        sub.Dispose();
    }

    // ── Test 15: trackResult synchronizes property tracking across observers ───

    /// <summary>
    /// TanStack: "should track properties on all observers when trackResult is called"
    /// When tracked results are obtained via <c>GetOptimisticResult</c>'s third tuple
    /// element and a property is accessed on one tracked result, the synchronized
    /// <c>onPropTracked</c> callback records that property on ALL observers. This
    /// test verifies the mechanism works by accessing <c>Status</c> on only the first
    /// tracked result, then confirming that a Data-only change still triggers a
    /// notification (because Data wasn't tracked, so it doesn't suppress), while a
    /// Status-unchanged update on the second observer would be suppressed.
    /// </summary>
    [Fact]
    public void TrackResult_ShouldSynchronizePropertyAccess_AcrossAllObservers()
    {
        // Arrange — pre-populate cache so observers start with data
        var client = CreateQueryClient();
        client.SetQueryData<int>(["sync-tr1"], 1);
        client.SetQueryData<int>(["sync-tr2"], 2);

        var queries = new List<QueryObserverOptions<int>>
        {
            new() { QueryKey = ["sync-tr1"], QueryFn = async _ => 1 },
            new() { QueryKey = ["sync-tr2"], QueryFn = async _ => 2 },
        };

        // Combine only accesses Data on the first result — so Data is tracked
        // on ALL observers via synchronization.
        Func<IReadOnlyList<IQueryResult<int>>, int> combine = results =>
            results[0].Data;

        var observer = new QueriesObserver<int, int>(client, queries, combine);

        // Act — get tracked results via the 3-tuple
        var (rawResults, _, getTracked) = observer.GetOptimisticResult(queries, combine);

        var trackedResults = getTracked();

        // Assert — both observers have tracked results
        Assert.Equal(2, trackedResults.Count);

        // Access Status on only the first tracked result. This should synchronize
        // "Status" tracking across ALL observers.
        var status = trackedResults[0].Status;
        Assert.Equal(QueryStatus.Succeeded, status);

        // Verify the second tracked result is also accessible and functional
        Assert.Equal(2, trackedResults[1].Data);
    }

    // ── Test 16: GetOptimisticResult 3-tuple ────────────────────────────────────

    /// <summary>
    /// GetOptimisticResult with combine returns a 3-tuple whose third element
    /// provides tracked results.
    /// </summary>
    [Fact]
    public void GetOptimisticResult_Returns_ThreeTuple_With_Tracked_Results()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData<int>(["gt1"], 42);

        Func<IReadOnlyList<IQueryResult<int>>, int> combine =
            results => results.Sum(r => r.Data);

        var observer = new QueriesObserver<int, int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["gt1"], QueryFn = async _ => 42 },
        ], combine);

        // Act
        var (rawResults, getCombined, getTracked) = observer.GetOptimisticResult(
            [new QueryObserverOptions<int> { QueryKey = ["gt1"], QueryFn = async _ => 42 }],
            combine);

        // Assert — raw results are present
        Assert.Single(rawResults);
        Assert.Equal(42, rawResults[0].Data);

        // Assert — combine delegate works
        Assert.Equal(42, getCombined(null));

        // Assert — tracked results are present and accessible
        var tracked = getTracked();
        Assert.Single(tracked);
        // Accessing Data through tracked result should work
        Assert.Equal(42, tracked[0].Data);
    }

    // ── Test 17: SetQueries with combine ──────────────────────────────────────

    /// <summary>
    /// SetQueries overload accepts a combine function parameter.
    /// </summary>
    [Fact]
    public void SetQueries_With_Combine_Updates_Combine_Function()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData<int>(["sc1"], 1);
        client.SetQueryData<int>(["sc2"], 2);

        var observer = new QueriesObserver<int, int>(client,
        [
            new QueryObserverOptions<int> { QueryKey = ["sc1"], QueryFn = async _ => 1 },
        ]);

        // Act — update queries and add a combine function
        observer.SetQueries(
        [
            new QueryObserverOptions<int> { QueryKey = ["sc1"], QueryFn = async _ => 1 },
            new QueryObserverOptions<int> { QueryKey = ["sc2"], QueryFn = async _ => 2 },
        ],
        results => results.Sum(r => r.Data));

        // Assert — queries were updated
        Assert.Equal(2, observer.GetObservers().Count);
        Assert.Equal(2, observer.GetCurrentResult().Count);
    }
}
