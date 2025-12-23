namespace RabstackQuery.Tests;

public class FetchInfiniteQueryTests
{
    [Fact]
    public async Task FetchInfiniteQueryAsync_NoCache_FetchesFirstPageAndReturnsInfiniteData()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Act
        var result = await client.FetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            InitialPageParam = 0,
            QueryFn = async ctx =>
            {
                fetchCount++;
                return $"page-{ctx.PageParam}";
            },
            GetNextPageParam = ctx => ctx.AllPages.Count < 3
                ? PageParamResult<int>.Some(ctx.PageParam + 1)
                : PageParamResult<int>.None,
        });

        // Assert
        Assert.Equal(1, result.Pages.Count);
        Assert.Equal("page-0", result.Pages[0]);
        Assert.Equal(0, result.PageParams[0]);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task FetchInfiniteQueryAsync_FreshCache_SkipsFetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var options = new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            StaleTime = TimeSpan.FromSeconds(60),
            InitialPageParam = 0,
            QueryFn = async ctx =>
            {
                fetchCount++;
                return $"page-{ctx.PageParam}";
            },
            GetNextPageParam = _ => PageParamResult<int>.None,
        };

        // First call populates cache
        await client.FetchInfiniteQueryAsync(options);

        // Act — second call with same staleTime should skip fetch
        var result = await client.FetchInfiniteQueryAsync(options);

        // Assert — should return cached data, fetch called only once
        Assert.Equal("page-0", result.Pages[0]);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task FetchInfiniteQueryAsync_StaleCache_Refetches()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var options = new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            StaleTime = TimeSpan.Zero, // Always stale
            InitialPageParam = 0,
            QueryFn = async ctx =>
            {
                fetchCount++;
                return $"fetch-{fetchCount}";
            },
            GetNextPageParam = _ => PageParamResult<int>.None,
        };

        // First call populates cache
        await client.FetchInfiniteQueryAsync(options);

        // Act — second call with staleTime=0 should refetch
        var result = await client.FetchInfiniteQueryAsync(options);

        // Assert — should have fetched twice
        Assert.Equal("fetch-2", result.Pages[0]);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task PrefetchInfiniteQueryAsync_SwallowsErrors()
    {
        var client = CreateQueryClient();

        // Act — should not throw
        var exception = await Record.ExceptionAsync(() =>
            client.PrefetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
            {
                QueryKey = ["fail"],
                InitialPageParam = 0,
                QueryFn = _ => throw new InvalidOperationException("boom"),
                GetNextPageParam = _ => PageParamResult<int>.None,
            }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PrefetchInfiniteQueryAsync_PopulatesCache()
    {
        var client = CreateQueryClient();

        await client.PrefetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            InitialPageParam = 0,
            QueryFn = async ctx => "prefetched",
            GetNextPageParam = _ => PageParamResult<int>.None,
        });

        // Assert — data should be in cache
        var data = client.GetQueryData<InfiniteData<string, int>>(["items"]);
        Assert.NotNull(data);
        Assert.Equal("prefetched", data.Pages[0]);
    }

    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_FreshCache_ReturnsCachedData()
    {
        // Arrange
        var client = CreateQueryClient();

        // Seed cache with infinite data
        client.SetQueryData(["items"], new InfiniteData<string, int>
        {
            Pages = ["seeded"],
            PageParams = [0],
        });

        var fetchCount = 0;

        // Act
        var result = await client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            StaleTime = TimeSpan.FromSeconds(60),
            InitialPageParam = 0,
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            },
            GetNextPageParam = _ => PageParamResult<int>.None,
        });

        // Assert — should return seeded data without fetching
        Assert.Equal("seeded", result.Pages[0]);
        Assert.Equal(0, fetchCount);
    }

    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_NoCache_Fetches()
    {
        var client = CreateQueryClient();

        var result = await client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            InitialPageParam = 0,
            QueryFn = async _ => "fetched",
            GetNextPageParam = _ => PageParamResult<int>.None,
        });

        Assert.Equal("fetched", result.Pages[0]);
    }

    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_InitialData_SeedsCache()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var initialData = new InfiniteData<string, int>
        {
            Pages = ["seeded-from-initial"],
            PageParams = [0],
        };

        // Act
        var result = await client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            InitialPageParam = 0,
            InitialData = initialData,
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            },
            GetNextPageParam = _ => PageParamResult<int>.None,
        });

        // Assert — should return initial data, not fetch
        Assert.Equal("seeded-from-initial", result.Pages[0]);
        Assert.Equal(0, fetchCount);

        // Verify cache was also seeded
        var cached = client.GetQueryData<InfiniteData<string, int>>(["items"]);
        Assert.NotNull(cached);
        Assert.Equal("seeded-from-initial", cached.Pages[0]);
    }

    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_RevalidateIfStale_ReturnsStaleDataAndRefetches()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["items"], new InfiniteData<string, int>
        {
            Pages = ["old-data"],
            PageParams = [0],
        });

        var fetchStarted = new TaskCompletionSource();
        var fetchCount = 0;

        // Act — staleTime=0 means always stale, RevalidateIfStale triggers background fetch
        var result = await client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["items"],
            StaleTime = TimeSpan.Zero,
            RevalidateIfStale = true,
            InitialPageParam = 0,
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                fetchStarted.TrySetResult();
                return "fresh-data";
            },
            GetNextPageParam = _ => PageParamResult<int>.None,
        });

        // Assert — should return stale data immediately
        Assert.Equal("old-data", result.Pages[0]);

        // Wait for background refetch to complete
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50); // Let state propagate

        // Verify cache was updated in the background
        var cached = client.GetQueryData<InfiniteData<string, int>>(["items"]);
        Assert.NotNull(cached);
        Assert.Equal("fresh-data", cached.Pages[0]);
        Assert.Equal(1, fetchCount);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
