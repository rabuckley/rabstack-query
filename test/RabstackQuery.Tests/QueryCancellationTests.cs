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

        await fetchTask;
    }

    [Fact]
    public async Task Cancel_WithoutRevert_MovesToErrorState_AndCanRefetch()
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
        Assert.Equal(QueryStatus.Errored, query.State.Status);
        Assert.IsAssignableFrom<OperationCanceledException>(query.State.Error);
        Assert.Equal("original", query.State.Data);

        query.SetQueryFn(_ => Task.FromResult("refetched"));
        await query.Fetch(TestContext.Current.CancellationToken);
        Assert.Null(query.State.Error);
        Assert.Equal("refetched", query.State.Data);
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

        await fetchTask;
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

    [Fact]
    public async Task Observer_RefetchAsync_WhenCancelledByCancelQueries_ResolvesWithRevertedResult()
    {
        var client = CreateQueryClient();
        client.SetQueryData(["refetch-cancelled"], "original");
        var fetchStarted = new TaskCompletionSource();
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["refetch-cancelled"],
            Enabled = false,
            QueryFn = async ctx =>
            {
                fetchStarted.TrySetResult();
                await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
                return "unreachable";
            },
        });

        var refetch = observer.RefetchAsync(cancellationToken: TestContext.Current.CancellationToken);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.CancelQueriesAsync(["refetch-cancelled"]);
        var result = await refetch.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("original", result.Data);
        Assert.Equal(FetchStatus.Idle, result.FetchStatus);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Observer_RefetchAsync_WhenQueryFnThrowsItsOwnCancellation_ReportsError()
    {
        var client = CreateQueryClient();
        var timeout = new TaskCanceledException("HttpClient.Timeout elapsed");
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["refetch-timeout"],
            Enabled = false,
            Retry = 0,
            QueryFn = _ => Task.FromException<string>(timeout),
        });

        var result = await observer.RefetchAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Same(timeout, result.Error);
        Assert.Equal(FetchStatus.Idle, result.FetchStatus);
    }

    [Fact]
    public async Task Observer_RefetchAsync_WithThrowOnError_WhenRevertedWithData_ResolvesWithRevertedResult()
    {
        var client = CreateQueryClient();
        client.SetQueryData(["throw-on-error-revert"], "original");
        var fetchStarted = new TaskCompletionSource();
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["throw-on-error-revert"],
            Enabled = false,
            QueryFn = async ctx =>
            {
                fetchStarted.TrySetResult();
                await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
                return "unreachable";
            },
        });

        var refetch = observer.RefetchAsync(new RefetchOptions { ThrowOnError = true }, TestContext.Current.CancellationToken);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.CancelQueriesAsync(["throw-on-error-revert"]);
        var result = await refetch.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("original", result.Data);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Fetch_WhenRevertedWithoutData_Throws_AndReturnsToPending()
    {
        var client = CreateQueryClient();
        var query = client.QueryCache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["revert-no-data"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        var fetchStarted = new TaskCompletionSource();
        query.SetQueryFn(async ctx =>
        {
            fetchStarted.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
            return "unreachable";
        });

        var fetch = query.Fetch(TestContext.Current.CancellationToken);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await query.Cancel(new CancelOptions { Revert = true });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    // Inline cancellation runs the old fetch's continuation before its replacement exists.
    // Awaiting the current fetch in that window would make it await itself.
    [Fact(Timeout = 10_000)]
    public async Task Fetch_WhenReplacedByRefetch_WaitsForReplacement_EvenWhenCancelCompletesInline()
    {
        var client = CreateQueryClient();
        client.SetQueryData(["replaced"], "initial");
        var query = client.QueryCache.Get<string>(DefaultQueryKeyHasher.Instance.HashQueryKey(["replaced"]))!;
        var replacementGate = new TaskCompletionSource<string>();
        var calls = 0;
        query.SetQueryFn(ctx =>
        {
            if (++calls == 2) return replacementGate.Task;
            var inlineCancel = new TaskCompletionSource<string>();
            ctx.CancellationToken.Register(() => inlineCancel.TrySetCanceled());
            return inlineCancel.Task;
        });

        var replaced = query.Fetch(TestContext.Current.CancellationToken);
        var replacement = query.Fetch(cancelRefetch: true, TestContext.Current.CancellationToken);
        var completedEarly = replaced.IsCompleted;
        replacementGate.SetResult("replacement-data");
        await replacement;
        await replaced;

        Assert.False(completedEarly);
        Assert.Equal(2, calls);
        Assert.Equal("replacement-data", query.State!.Data);
    }

    [Fact(Timeout = 10_000)]
    public async Task Fetch_WhenReplacementFails_RejectsReplacedCallerToo()
    {
        var client = CreateQueryClient();
        client.SetQueryDefaults(["replaced-fails"], new QueryDefaults { Retry = 0 });
        client.SetQueryData(["replaced-fails"], "initial");
        var query = client.QueryCache.Get<string>(DefaultQueryKeyHasher.Instance.HashQueryKey(["replaced-fails"]))!;
        var calls = 0;
        var failure = new InvalidOperationException("replacement failed");
        query.SetQueryFn(async ctx =>
        {
            if (++calls == 2) throw failure;
            await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
            return "unreachable";
        });

        var replaced = query.Fetch(TestContext.Current.CancellationToken);
        var replacement = query.Fetch(cancelRefetch: true, TestContext.Current.CancellationToken);
        var replacedError = await Assert.ThrowsAsync<InvalidOperationException>(() => replaced);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replacement);

        Assert.Same(failure, replacedError);
    }

    // No TanStack equivalent (its fetch takes no token). Closest API is cancelQueries, whose default
    // is revert: the caller gets the .NET contract's exception and the query must not stay fetching.
    [Fact]
    public async Task Fetch_WhenCallersTokenCancelled_Throws_AndRevertsState()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["caller-token"], "original");
        var query = client.QueryCache.Get<string>(DefaultQueryKeyHasher.Instance.HashQueryKey(["caller-token"]))!;
        var fetchStarted = new TaskCompletionSource();
        query.SetQueryFn(async ctx =>
        {
            fetchStarted.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
            return "unreachable";
        });
        using var cts = new CancellationTokenSource();

        // Act
        var fetch = query.Fetch(cts.Token);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.Equal("original", query.State.Data);
    }

    [Fact]
    public async Task Reset_DuringFetch_DoesNotLeaveErrorState()
    {
        var client = CreateQueryClient();
        var query = client.QueryCache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["reset-during-fetch"], GcTime = TimeSpan.FromMinutes(5), Retry = 0 });
        var fetchStarted = new TaskCompletionSource();
        query.SetQueryFn(async ctx =>
        {
            fetchStarted.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
            return "unreachable";
        });

        var fetch = query.Fetch(TestContext.Current.CancellationToken);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        query.Reset();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);

        Assert.Equal(QueryStatus.Pending, query.State!.Status);
        Assert.Null(query.State.Error);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
