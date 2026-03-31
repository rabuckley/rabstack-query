namespace RabstackQuery;

public class QueryCancellationTests
{
    [Fact]
    public async Task Cancel_InFlightFetch_ThrowsOperationCancelled()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });

        var fetchStarted = new TaskCompletionSource();

        query.SetQueryFn(async ctx =>
        {
            fetchStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "data";
        });

        // Act — start fetch, then cancel
        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        await query.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
    }

    [Fact]
    public async Task Cancel_WithRevert_RestoresPreFetchState()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "original");

        var cache = client.QueryCache;
        var query = cache.Get<string>(
            DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]))!;

        var fetchStarted = new TaskCompletionSource();

        query.SetQueryFn(async ctx =>
        {
            fetchStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "new-data";
        });

        // Act
        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        // State should be Fetching now
        Assert.Equal(FetchStatus.Fetching, query.State!.FetchStatus);

        await query.Cancel(new CancelOptions { Revert = true });

        // Assert — state reverted to pre-fetch
        Assert.Equal("original", query.State!.Data);
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);

        // The fetch task should throw OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
    }

    [Fact]
    public async Task Cancel_WithoutRevert_KeepsFetchingState()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "original");

        var cache = client.QueryCache;
        var query = cache.Get<string>(
            DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]))!;

        var fetchStarted = new TaskCompletionSource();

        query.SetQueryFn(async ctx =>
        {
            fetchStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "new-data";
        });

        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        // Act — cancel without revert
        await query.Cancel(new CancelOptions { Revert = false });

        // Assert — state was NOT reverted (still in fetching state)
        Assert.Equal(FetchStatus.Fetching, query.State!.FetchStatus);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
    }

    [Fact]
    public async Task Cancel_NoActiveRetryer_IsNoOp()
    {
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["idle"], GcTime = TimeSpan.FromMinutes(5) });

        // Act — cancelling when nothing is in flight should be safe
        await query.Cancel();

        // Assert — state is still initial
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
    }

    [Fact]
    public async Task CancelQueriesAsync_BulkCancel_WithFilters()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;

        var query1 = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 1], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        var query2 = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["todos", 2], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["users"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });

        var started1 = new TaskCompletionSource();
        var started2 = new TaskCompletionSource();

        query1.SetQueryFn(async ctx =>
        {
            started1.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "t1";
        });
        query2.SetQueryFn(async ctx =>
        {
            started2.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "t2";
        });

        var fetch1 = query1.Fetch();
        var fetch2 = query2.Fetch();

        await Task.WhenAll(started1.Task, started2.Task);

        // Act — cancel all "todos" queries
        await client.CancelQueriesAsync(new QueryFilters { QueryKey = ["todos"] });

        // Assert — both should be cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch2);
    }

    [Fact]
    public async Task Fetch_Cancellation_DoesNotDispatchErrorAction()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "existing");

        var cache = client.QueryCache;
        var query = cache.Get<string>(
            DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]))!;

        var fetchStarted = new TaskCompletionSource();

        query.SetQueryFn(async ctx =>
        {
            fetchStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            return "new";
        });

        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        // Cancel with revert
        await query.Cancel(new CancelOptions { Revert = true });

        try { await fetchTask; } catch (OperationCanceledException) { }

        // Assert — status should NOT be Errored; it should be reverted to Succeeded
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Null(query.State.Error);
    }

    // ── AbortSignalConsumed tests ──────────────────────────────────────
    // Mirrors TanStack query.test.tsx:252-311.

    [Fact]
    public async Task RemoveObserver_SignalNotConsumed_FetchContinues()
    {
        // When the query function does NOT read ctx.CancellationToken, removing
        // the last observer should soft-cancel (CancelRetry) — the current fetch
        // attempt finishes and its result is cached. Mirrors TanStack
        // query.test.tsx:252-279.

        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;
        var fetchStarted = new TaskCompletionSource();
        var fetchGate = new TaskCompletionSource<string>();

        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["signal-not-consumed"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });

        // Query function ignores the context (discards _) — signal NOT consumed.
        query.SetQueryFn(async _ =>
        {
            fetchStarted.SetResult();
            return await fetchGate.Task;
        });

        // Subscribe an observer so RemoveObserver has something to remove.
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["signal-not-consumed"],
        });
        using var sub = observer.Subscribe(_ => { });

        // Act — start a fetch, then remove the observer
        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        sub.Dispose();

        // Complete the fetch — since signal was NOT consumed, the fetch should
        // continue and cache its result (not throw).
        fetchGate.SetResult("completed-data");
        await fetchTask;

        // Assert — the result was cached successfully
        Assert.Equal("completed-data", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
    }

    [Fact]
    public async Task RemoveObserver_SignalConsumed_FetchCancelledWithRevert()
    {
        // When the query function reads ctx.CancellationToken, removing the last
        // observer should hard-cancel with state revert. Mirrors TanStack
        // query.test.tsx:281-311.

        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["signal-consumed"], "original-data");

        var cache = client.QueryCache;
        var query = cache.Get<string>(
            DefaultQueryKeyHasher.Instance.HashQueryKey(["signal-consumed"]))!;

        var fetchStarted = new TaskCompletionSource();

        // Query function reads ctx.CancellationToken — signal IS consumed.
        query.SetQueryFn(async ctx =>
        {
            fetchStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ctx.CancellationToken);
            return "new-data";
        });

        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["signal-consumed"],
        });
        using var sub = observer.Subscribe(_ => { });

        // Act — start a fetch, then remove the observer
        var fetchTask = query.Fetch();
        await fetchStarted.Task;

        sub.Dispose();

        // Assert — the fetch should be cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);

        // State should be reverted to the pre-fetch state
        Assert.Equal("original-data", query.State!.Data);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    [Fact]
    public async Task RemoveObserver_SignalConsumed_ThreadSafety()
    {
        // RemoveObserver racing with fetch completion should not corrupt state.
        // Arrange
        var client = CreateQueryClient();
        var cache = client.QueryCache;
        var fetchGate = new TaskCompletionSource();

        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["race-test"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });

        query.SetQueryFn(async ctx =>
        {
            fetchGate.SetResult();
            await Task.Delay(TimeSpan.FromMilliseconds(50), ctx.CancellationToken);
            return "data";
        });

        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["race-test"],
        });
        using var sub = observer.Subscribe(_ => { });

        // Act — start fetch, wait for it to begin, then race disposal
        var fetchTask = query.Fetch();
        await fetchGate.Task;

        // Dispose immediately — races with the 50ms delay in the fetch
        sub.Dispose();

        // Assert — either the fetch completes or is cancelled, but no exception
        // should escape other than OperationCanceledException.
        try
        {
            await fetchTask;
        }
        catch (OperationCanceledException)
        {
            // Expected if disposal won the race
        }

        // State should be consistent regardless of race outcome
        Assert.NotNull(query.State);
        Assert.True(
            query.State.Status is QueryStatus.Succeeded or QueryStatus.Pending,
            $"Unexpected status: {query.State.Status}");
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
