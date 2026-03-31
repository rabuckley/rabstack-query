namespace RabstackQuery;

/// <summary>
/// Tests for <see cref="InfiniteQueryBehavior"/> — the page-fetching algorithm.
/// Ports TanStack's <c>infiniteQueryBehavior.test.tsx</c> tests where applicable
/// and adds C#-specific tests for value-type TPageParam and cancellation.
/// </summary>
public sealed class InfiniteQueryBehaviorTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public async Task MaxPages_Should_Limit_Stored_Pages_Forward()
    {
        // Arrange — MaxPages = 2, fetch 3 pages forward
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string[], int>
        {
            QueryKey = ["max-pages-forward"],
            QueryFn = ctx => Task.FromResult(new[] { $"page-{ctx.PageParam}" }),
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
            MaxPages = 2,
        };

        var observer = new InfiniteQueryObserver<string[], int>(client, options);
        var resultTcs = new TaskCompletionSource<IInfiniteQueryResult<string[], int>>();

        // Act
        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess && result.Data?.Pages.Count > 0)
            {
                resultTcs.TrySetResult(result);
            }
        });

        // Wait for initial fetch
        var initialResult = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(initialResult.Data!.Pages);

        // Fetch next page twice
        await observer.FetchNextPageAsync();
        await observer.FetchNextPageAsync();

        // Assert — should have only 2 pages due to MaxPages
        var finalResult = observer.CurrentResult;
        Assert.Equal(2, finalResult.Data!.Pages.Count);

        // Pages should be the last 2 (page-1 and page-2), not page-0
        Assert.Equal("page-1", finalResult.Data.Pages[0][0]);
        Assert.Equal("page-2", finalResult.Data.Pages[1][0]);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task MaxPages_Should_Limit_Stored_Pages_Backward()
    {
        // Arrange — MaxPages = 2, fetch pages backward
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string[], int>
        {
            QueryKey = ["max-pages-backward"],
            QueryFn = ctx => Task.FromResult(new[] { $"page-{ctx.PageParam}" }),
            InitialPageParam = 5,
            GetNextPageParam = ctx => ctx.PageParam + 1,
            GetPreviousPageParam = ctx => ctx.PageParam > 0
                ? ctx.PageParam - 1
                : PageParamResult<int>.None,
            MaxPages = 2,
        };

        var observer = new InfiniteQueryObserver<string[], int>(client, options);
        var resultTcs = new TaskCompletionSource<IInfiniteQueryResult<string[], int>>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess)
                resultTcs.TrySetResult(result);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — fetch 2 previous pages
        await observer.FetchPreviousPageAsync();
        await observer.FetchPreviousPageAsync();

        // Assert — should have 2 pages, the most recent backward fetches
        var finalResult = observer.CurrentResult;
        Assert.Equal(2, finalResult.Data!.Pages.Count);

        // Pages should be page-3 and page-4 (dropped page-5 from the end)
        Assert.Equal("page-3", finalResult.Data.Pages[0][0]);
        Assert.Equal("page-4", finalResult.Data.Pages[1][0]);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Should_Stop_When_GetNextPageParam_Returns_None()
    {
        // Arrange — 3 total pages
        var client = CreateQueryClient();
        var fetchCount = 0;
        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["stop-at-none"],
            QueryFn = ctx =>
            {
                fetchCount++;
                return Task.FromResult($"page-{ctx.PageParam}");
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam < 2
                ? ctx.PageParam + 1
                : PageParamResult<int>.None,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — fetch until no more pages
        await observer.FetchNextPageAsync(); // page 1
        await observer.FetchNextPageAsync(); // page 2
        await observer.FetchNextPageAsync(); // should be no-op, no more pages

        // Assert
        var result = observer.CurrentResult;
        Assert.Equal(3, result.Data!.Pages.Count);
        Assert.False(result.HasNextPage);
        Assert.Equal(3, fetchCount); // only 3 actual fetches

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Page_Params_Should_Be_Passed_Correctly_Via_PageParamContext()
    {
        // Arrange — track the page param contexts received
        var client = CreateQueryClient();
        var receivedContexts = new List<PageParamContext<string, int>>();
        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["page-param-context"],
            QueryFn = ctx => Task.FromResult($"data-{ctx.PageParam}"),
            InitialPageParam = 10,
            GetNextPageParam = ctx =>
            {
                receivedContexts.Add(ctx);
                return ctx.PageParam + 10;
            },
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await observer.FetchNextPageAsync();

        // Assert — GetNextPageParam is called to determine the next page param.
        // The first call (during HasNextPage computation or FetchNextPage) has the
        // first page's data. The call that triggers the actual fetch for page 2
        // should have received the first page's context.
        Assert.True(receivedContexts.Count >= 1);

        // The first invocation should have the initial page data
        var firstCtx = receivedContexts[0];
        Assert.Equal("data-10", firstCtx.Page);
        Assert.Single(firstCtx.AllPages);
        Assert.Equal(10, firstCtx.PageParam);
        Assert.Single(firstCtx.AllPageParams);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Value_Type_PageParam_Should_Distinguish_None_From_Zero()
    {
        // Arrange — int TPageParam where 0 is a valid page param
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["value-type-page-param"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 0,
            // 0 is a valid page param, only stop at page 2
            GetNextPageParam = ctx => ctx.PageParam < 2
                ? ctx.PageParam + 1
                : PageParamResult<int>.None,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await observer.FetchNextPageAsync(); // page 1
        await observer.FetchNextPageAsync(); // page 2

        // Assert
        var result = observer.CurrentResult;
        Assert.Equal(3, result.Data!.Pages.Count);
        Assert.Equal("page-0", result.Data.Pages[0]);
        Assert.Equal(0, result.Data.PageParams[0]); // 0 was a valid param, not None
        Assert.False(result.HasNextPage);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public async Task Cancellation_Should_Stop_Page_Fetching_During_Refetch()
    {
        // Arrange — query that blocks on a gate during the refetch phase.
        // The gate is only armed after initial setup completes so the initial
        // fetch and FetchNextPageAsync proceed without blocking.
        var client = CreateQueryClient();
        var cts = new CancellationTokenSource();
        var secondPageStarted = new TaskCompletionSource<bool>();
        var blockDuringRefetch = false;

        var options = new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["cancel-refetch"],
            QueryFn = async ctx =>
            {
                if (blockDuringRefetch && ctx.PageParam > 0)
                {
                    secondPageStarted.TrySetResult(true);
                    // Block until cancelled via CancellationToken instead of Task.Delay.
                    var neverCompletes = new TaskCompletionSource<bool>();
                    await using (ctx.CancellationToken.Register(() => neverCompletes.TrySetCanceled()))
                    {
                        await neverCompletes.Task;
                    }
                }
                return $"page-{ctx.PageParam}";
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        };

        var observer = new InfiniteQueryObserver<string, int>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Fetch a second page so there are 2 pages to refetch
        await observer.FetchNextPageAsync();

        // Arm the blocking gate for the refetch phase
        blockDuringRefetch = true;

        // Act — trigger a refetch with cancellation token, then cancel mid-refetch
        var refetchResult = observer.CurrentResult;
        var refetchTask = refetchResult.RefetchAsync(cancellationToken: cts.Token);

        // Wait for second page fetch to start, then cancel
        await secondPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        // Assert — refetch should throw a cancellation exception.
        // TaskCanceledException (from TCS.TrySetCanceled) is a subclass of
        // OperationCanceledException, so use IsAssignableFrom for robustness.
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refetchTask);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task InitialPageParam_Can_Be_Null_For_Reference_Type()
    {
        // Arrange — string? TPageParam with null initial value
        var client = CreateQueryClient();
        var options = new InfiniteQueryObserverOptions<string, string?>
        {
            QueryKey = ["null-initial-param"],
            QueryFn = ctx => Task.FromResult($"data-for-{ctx.PageParam ?? "null"}"),
            InitialPageParam = null,
            GetNextPageParam = ctx => ctx.AllPages.Count < 2
                ? (PageParamResult<string?>)$"cursor-{ctx.AllPages.Count}"
                : PageParamResult<string?>.None,
        };

        var observer = new InfiniteQueryObserver<string, string?>(client, options);
        var resultTcs = new TaskCompletionSource<bool>();

        using var sub = observer.Subscribe(result =>
        {
            if (result.IsSuccess) resultTcs.TrySetResult(true);
        });

        await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await observer.FetchNextPageAsync();

        // Assert
        var result = observer.CurrentResult;
        Assert.Equal(2, result.Data!.Pages.Count);
        Assert.Equal("data-for-null", result.Data.Pages[0]);
        Assert.Null(result.Data.PageParams[0]);

        // No explicit disposal needed — subscription disposal (or lack of subscription)
        // handles cleanup via OnUnsubscribe → Destroy() cascade.
    }

    [Fact]
    public void AddToEnd_Should_Respect_MaxItems()
    {
        // Arrange
        IReadOnlyList<int> items = [1, 2, 3];

        // Act
        var result = InfiniteQueryBehavior.AddToEnd(items, 4, maxItems: 3);

        // Assert — oldest item dropped
        Assert.Equal([2, 3, 4], result);
    }

    [Fact]
    public void AddToStart_Should_Respect_MaxItems()
    {
        // Arrange
        IReadOnlyList<int> items = [1, 2, 3];

        // Act
        var result = InfiniteQueryBehavior.AddToStart(items, 0, maxItems: 3);

        // Assert — newest item from the end dropped
        Assert.Equal([0, 1, 2], result);
    }

    [Fact]
    public void AddToEnd_Without_MaxItems_Should_Append()
    {
        IReadOnlyList<int> items = [1, 2];
        var result = InfiniteQueryBehavior.AddToEnd(items, 3);
        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void AddToStart_Without_MaxItems_Should_Prepend()
    {
        IReadOnlyList<int> items = [2, 3];
        var result = InfiniteQueryBehavior.AddToStart(items, 1);
        Assert.Equal([1, 2, 3], result);
    }
}
