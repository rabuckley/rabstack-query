namespace RabstackQuery;

/// <summary>
/// Tests for <see cref="InfiniteQueryObserver{TData,TPageParam}"/>.
/// Ports TanStack's <c>infiniteQueryObserver.test.tsx</c> tests and adds
/// C#-specific tests for concurrent dedup and disposal.
/// </summary>
public sealed class InfiniteQueryObserverTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public void Should_Return_Initial_State_On_First_Result()
    {
        // Arrange — test checks initial state before any fetch, so the query
        // function's behavior doesn't matter.
        var client = CreateQueryClient();

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["initial-state"],
            QueryFn = ctx => Task.FromResult("data"),
            InitialPageParam = 0,
            GetNextPageParam = _ => PageParamResult<int>.None,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);

        // Act — get result before first fetch completes
        var result = observer.CurrentResult;

        // Assert — should be in pending state with no data
        Assert.Equal(QueryStatus.Pending, result.Status);
        Assert.Null(result.Data);
        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task Should_Compute_HasNextPage_And_HasPreviousPage()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["has-page"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 5,
            GetNextPageParam = ctx => ctx.PageParam < 10
                ? ctx.PageParam + 1
                : PageParamResult<int>.None,
            GetPreviousPageParam = ctx => ctx.PageParam > 0
                ? ctx.PageParam - 1
                : PageParamResult<int>.None,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — initial page at 5: has both next and previous
        var result = observer.CurrentResult;
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Should_Report_IsFetchingNextPage_During_Forward_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<bool>();
        var isFetchingNextPageSeen = false;

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["fetching-next"],
            QueryFn = async ctx =>
            {
                if (ctx.PageParam > 0)
                {
                    await fetchGate.Task.WaitAsync(ctx.CancellationToken);
                }
                return $"page-{ctx.PageParam}";
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var initialFetchDone = new TaskCompletionSource<bool>();
        var fetchingNextPageSeen = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess && result.Data?.Pages.Count == 1)
                initialFetchDone.TrySetResult(true);
            if (result.IsFetchingNextPage)
            {
                isFetchingNextPageSeen = true;
                fetchingNextPageSeen.TrySetResult(true);
            }
        });

        await initialFetchDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — start fetching next page (will block on fetchGate)
        var fetchTask = observer.FetchNextPageAsync();

        // Wait for the subscription to observe the fetching state
        await fetchingNextPageSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — should be fetching next page
        var midResult = observer.CurrentResult;
        Assert.True(midResult.IsFetchingNextPage);
        Assert.False(midResult.IsFetchingPreviousPage);

        // Complete the fetch
        fetchGate.SetResult(true);
        await fetchTask;

        // After completion
        var finalResult = observer.CurrentResult;
        Assert.False(finalResult.IsFetchingNextPage);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Select_Should_Transform_InfiniteData()
    {
        // Arrange — select transform that uppercases page data
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["select-transform"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
            Select = data => new InfiniteData<string, int>
            {
                Pages = data.Pages.Select(p => p.ToUpperInvariant()).ToList(),
                PageParams = data.PageParams,
            },
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — data should be uppercased by the Select transform
        var result = observer.CurrentResult;
        Assert.Equal("PAGE-0", result.Data!.Pages[0]);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public void Should_Not_Invoke_PageParam_Functions_On_Empty_Pages()
    {
        // Arrange — track invocations of page param functions
        var client = CreateQueryClient();
        var getNextCalled = false;
        var getPrevCalled = false;

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["empty-pages-no-invoke"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 0,
            GetNextPageParam = ctx =>
            {
                getNextCalled = true;
                return ctx.PageParam + 1;
            },
            GetPreviousPageParam = ctx =>
            {
                getPrevCalled = true;
                return PageParamResult<int>.None;
            },
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);

        // Act — get result before any fetch
        var result = observer.CurrentResult;

        // Assert — no data yet, so page param functions should not be called
        // for HasNextPage/HasPreviousPage computation
        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
        Assert.False(getNextCalled);
        Assert.False(getPrevCalled);
    }

    [Fact]
    public async Task Concurrent_FetchNextPage_Should_Be_Deduplicated()
    {
        // Arrange — query function that blocks on a gate so both FetchNextPage
        // calls see the fetch in-flight. The gate ensures the fetch stays
        // in Fetching status while the second call checks dedup.
        var client = CreateQueryClient();
        var fetchCount = 0;
        var fetchGate = new TaskCompletionSource<bool>();

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["concurrent-dedup"],
            QueryFn = async ctx =>
            {
                Interlocked.Increment(ref fetchCount);
                // Block all next-page fetches on the gate
                if (ctx.PageParam > 0)
                {
                    await fetchGate.Task;
                }
                return $"page-{ctx.PageParam}";
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Yield to allow FetchCore's finally block to complete cleanup
        // (_retryer, _currentFetchTask reset). Without this, the inline
        // continuation from TaskCompletionSource runs before FetchCore finishes.
        await Task.Yield();

        fetchCount = 0; // reset after initial fetch

        // Act — first call starts a fetch that blocks on the gate; second
        // call should see the in-flight fetch and dedup.
        var task1 = observer.FetchNextPageAsync();
        var task2 = observer.FetchNextPageAsync();

        // Release the gate so the fetch completes
        fetchGate.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert — only one actual fetch should have happened due to dedup
        Assert.Equal(1, fetchCount);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Unsubscribe_During_Fetch_Cancels_When_Token_Consumed()
    {
        // Arrange — the query function reads ctx.CancellationToken, which sets the
        // abort-signal-consumed flag. When all observers unsubscribe, this triggers
        // a hard cancel (with state revert) rather than a soft CancelRetry.
        // Mirrors TanStack's behavior from query.ts:354-366.
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<bool>();

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["unsubscribe-during-fetch"],
            QueryFn = async ctx =>
            {
                if (ctx.PageParam > 0)
                {
                    await fetchGate.Task.WaitAsync(ctx.CancellationToken);
                }
                return $"page-{ctx.PageParam}";
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — start a fetch and unsubscribe while it's in-flight.
        var fetchTask = observer.FetchNextPageAsync();
        sub.Dispose();

        // Assert — the fetch should be cancelled because the query function
        // consumed the CancellationToken.
        await Assert.ThrowsAsync<TaskCanceledException>(() => fetchTask);
    }

    [Fact]
    public async Task Should_Refetch_All_Pages_On_Invalidation()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["refetch-on-invalidate"],
            QueryFn = ctx =>
            {
                fetchCount++;
                return Task.FromResult($"page-{ctx.PageParam}-v{fetchCount}");
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam < 2
                ? ctx.PageParam + 1
                : PageParamResult<int>.None,
            StaleTime = TimeSpan.Zero,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Fetch second page
        await observer.FetchNextPageAsync();
        var beforeRefetch = fetchCount;

        // Signal when refetch produces updated data (fetchCount > beforeRefetch
        // means pages were re-fetched with new version strings)
        var refetchDone = new TaskCompletionSource<bool>();
        using var refetchSub = observer.Subscribe(result =>
        {
            if (result.IsSuccess && fetchCount > beforeRefetch)
                refetchDone.TrySetResult(true);
        });

        // Act — invalidate triggers a refetch of all pages
        await client.InvalidateQueriesAsync(["refetch-on-invalidate"]);

        await refetchDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — should have re-fetched both pages (2 more fetches)
        var result = observer.CurrentResult;
        Assert.Equal(2, result.Data!.Pages.Count);
        Assert.True(fetchCount > beforeRefetch);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task IsRefetching_Should_Exclude_Page_Fetches()
    {
        // Arrange — mirrors infiniteQueryObserver.ts:177–181
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<bool>();

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["refetching-exclude-page"],
            QueryFn = async ctx =>
            {
                if (ctx.PageParam > 0)
                {
                    await fetchGate.Task.WaitAsync(ctx.CancellationToken);
                }
                return $"page-{ctx.PageParam}";
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();
        var fetchingNextSeen = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
            if (result.IsFetchingNextPage) fetchingNextSeen.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — start fetching next page
        var fetchTask = observer.FetchNextPageAsync();

        await fetchingNextSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — IsFetchingNextPage should be true, but IsRefetching should be false
        // because a directional page fetch is not a refetch.
        var midResult = observer.CurrentResult;
        Assert.True(midResult.IsFetchingNextPage);
        Assert.False(midResult.IsRefetching);

        // Cleanup
        fetchGate.SetResult(true);
        await fetchTask;

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Refetch_Should_Shrink_Pages_When_GetNextPageParam_Returns_None()
    {
        // Arrange — first fetch has 3 pages, refetch GetNextPageParam returns None after 1
        var client = CreateQueryClient();
        var fetchRound = 0;

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["shrink-on-refetch"],
            QueryFn = ctx =>
            {
                return Task.FromResult($"page-{ctx.PageParam}-r{fetchRound}");
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx =>
            {
                // During initial load, allow 3 pages. During refetch, only 1.
                var maxPages = fetchRound == 0 ? 2 : 0;
                return ctx.PageParam < maxPages
                    ? ctx.PageParam + 1
                    : PageParamResult<int>.None;
            },
            StaleTime = TimeSpan.Zero,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Fetch 2 more pages
        await observer.FetchNextPageAsync();
        await observer.FetchNextPageAsync();
        Assert.Equal(3, observer.CurrentResult.Data!.Pages.Count);

        // Act — refetch with GetNextPageParam returning None after page 0
        fetchRound = 1;
        var refetchResult = observer.CurrentResult;
        await refetchResult.RefetchAsync();

        // Assert — pages should have shrunk to just 1
        var result = observer.CurrentResult;
        Assert.Single(result.Data!.Pages);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }
}
