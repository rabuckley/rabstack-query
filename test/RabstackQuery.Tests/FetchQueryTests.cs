namespace RabstackQuery.Tests;

public class FetchQueryTests
{
    [Fact]
    public async Task FetchQueryAsync_NoCache_FetchesAndReturnsData()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Act
        var result = await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            }
        });

        // Assert
        Assert.Equal("fetched", result);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task FetchQueryAsync_FreshCache_SkipsFetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // First call populates cache
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.FromSeconds(60), // 60 seconds — data won't be stale
            QueryFn = async _ =>
            {
                fetchCount++;
                return "cached";
            }
        });

        // Act — second call with same staleTime should skip fetch
        var result = await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.FromSeconds(60),
            QueryFn = async _ =>
            {
                fetchCount++;
                return "new-data";
            }
        });

        // Assert — should return cached data, fetch called only once
        Assert.Equal("cached", result);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task FetchQueryAsync_StaleCache_Refetches()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // First call populates cache
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.Zero, // Always stale
            QueryFn = async _ =>
            {
                fetchCount++;
                return $"fetch-{fetchCount}";
            }
        });

        // Act — second call with staleTime=0 should refetch
        var result = await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.Zero,
            QueryFn = async _ =>
            {
                fetchCount++;
                return $"fetch-{fetchCount}";
            }
        });

        // Assert — should have fetched twice
        Assert.Equal("fetch-2", result);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task FetchQueryAsync_Error_Propagates()
    {
        var client = CreateQueryClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.FetchQueryAsync(new FetchQueryOptions<string>
            {
                QueryKey = ["fail"],
                QueryFn = _ => throw new InvalidOperationException("boom")
            }));
    }

    [Fact]
    public async Task PrefetchQueryAsync_SwallowsErrors()
    {
        var client = CreateQueryClient();

        // Act — should not throw
        var exception = await Record.ExceptionAsync(() =>
            client.PrefetchQueryAsync(new FetchQueryOptions<string>
            {
                QueryKey = ["fail"],
                QueryFn = _ => throw new InvalidOperationException("boom")
            }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PrefetchQueryAsync_PopulatesCache()
    {
        var client = CreateQueryClient();

        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ => "prefetched"
        });

        // Assert — data should be in cache
        var data = client.GetQueryData<string>(["todos"]);
        Assert.Equal("prefetched", data);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_FreshCache_ReturnsCachedData()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "seeded");

        var fetchCount = 0;

        // Act
        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.FromSeconds(60), // Fresh for 60 seconds
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            }
        });

        // Assert — should return seeded data without fetching
        Assert.Equal("seeded", result);
        Assert.Equal(0, fetchCount);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_NoCache_Fetches()
    {
        var client = CreateQueryClient();

        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ => "fetched"
        });

        Assert.Equal("fetched", result);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_StaleCache_Fetches()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["todos"], "old");

        var fetchCount = 0;

        // Act — staleTime=0 means always stale
        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            StaleTime = TimeSpan.Zero,
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fresh";
            }
        });

        // Assert
        Assert.Equal("fresh", result);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_Should_Return_Cached_Falsy_Data()
    {
        // TanStack's ensureQueryData returns cached data even when it's "falsy"
        // (empty string, 0, etc.) — only truly missing data triggers a fetch.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["empty"], "");

        var fetchCount = 0;

        // Act
        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["empty"],
            StaleTime = TimeSpan.FromSeconds(60),
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            }
        });

        // Assert — should return the empty string, not fetch
        Assert.Equal("", result);
        Assert.Equal(0, fetchCount);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_Should_Not_Fetch_With_InitialData()
    {
        // When InitialData is provided and there's no cached data,
        // EnsureQueryDataAsync should use it and skip fetching.
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Act
        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["with-initial"],
            InitialData = "seeded-from-initial",
            QueryFn = async _ =>
            {
                fetchCount++;
                return "fetched";
            }
        });

        // Assert — should return initial data, not fetch
        Assert.Equal("seeded-from-initial", result);
        Assert.Equal(0, fetchCount);

        // Verify cache was also seeded
        var cached = client.GetQueryData<string>(["with-initial"]);
        Assert.Equal("seeded-from-initial", cached);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_Should_Revalidate_In_Background_When_Stale()
    {
        // When RevalidateIfStale=true and cached data exists but is stale,
        // return cached data immediately and trigger a background refetch.
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["stale-revalidate"], "old-data");

        var fetchStarted = new TaskCompletionSource();
        var fetchCount = 0;

        // Act — staleTime=0 means always stale, RevalidateIfStale triggers background fetch
        var result = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["stale-revalidate"],
            StaleTime = TimeSpan.Zero,
            RevalidateIfStale = true,
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                fetchStarted.TrySetResult();
                return "fresh-data";
            }
        });

        // Assert — should return stale data immediately
        Assert.Equal("old-data", result);

        // Wait for background refetch to complete
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50); // Let state propagate

        // Verify cache was updated in the background
        var cached = client.GetQueryData<string>(["stale-revalidate"]);
        Assert.Equal("fresh-data", cached);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task PrefetchQueryAsync_Should_Be_GarbageCollected_After_GcTime()
    {
        // Prefetched queries with a short GcTime should be removed from the cache
        // after the GC timer fires.
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: fakeTime);

        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["gc-test"],
            GcTime = TimeSpan.FromMilliseconds(10),
            QueryFn = async _ => "prefetched"
        });

        // Verify data is in cache
        Assert.Equal("prefetched", client.GetQueryData<string>(["gc-test"]));

        // Act — advance time past GcTime
        fakeTime.Advance(TimeSpan.FromMilliseconds(50));

        // Assert — data should be garbage collected
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["gc-test"]);
        var query = client.GetQueryCache().Get<string>(queryHash);
        Assert.Null(query);
    }

    [Fact]
    public async Task FetchQuery_Should_Only_Fetch_When_Data_Exceeds_StaleTime()
    {
        // TanStack line 734: staleTime controls whether FetchQueryAsync returns
        // cached data or refetches based on data age.
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: fakeTime);
        var count = 0;

        client.SetQueryData(["key"], count);

        // Act & Assert

        // 1. staleTime=100ms, data age=0 → fresh, returns cached 0
        var result1 = await client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = ["key"],
            StaleTime = TimeSpan.FromMilliseconds(100),
            QueryFn = async _ => ++count
        });
        Assert.Equal(0, result1);

        // 2. Advance 10ms. staleTime=10ms, data age=10ms → stale (10 >= 10), refetches
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        var result2 = await client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = ["key"],
            StaleTime = TimeSpan.FromMilliseconds(10),
            QueryFn = async _ => ++count
        });
        Assert.Equal(1, result2);

        // 3. Immediately after, staleTime=10ms, data age=0 → fresh, returns cached
        var result3 = await client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = ["key"],
            StaleTime = TimeSpan.FromMilliseconds(10),
            QueryFn = async _ => ++count
        });
        Assert.Equal(1, result3);

        // 4. Advance 10ms. staleTime=10ms, data age=10ms → stale, refetches
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        var result4 = await client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = ["key"],
            StaleTime = TimeSpan.FromMilliseconds(10),
            QueryFn = async _ => ++count
        });
        Assert.Equal(2, result4);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
