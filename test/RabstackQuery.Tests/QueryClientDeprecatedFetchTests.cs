#pragma warning disable CS0618 // Covers the obsolete imperative fetch methods.

using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

public sealed class QueryClientDeprecatedFetchTests
{
    private static readonly QueryKey Key = ["key"];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly QueryClient _client;
    private int _fetchCount;

    public QueryClientDeprecatedFetchTests()
    {
        _client = new QueryClient(new QueryCache(), timeProvider: _time);
    }

    private Func<QueryFunctionContext, Task<T>> Returning<T>(T value) => _ =>
    {
        _fetchCount++;
        return Task.FromResult(value);
    };

    private Func<InfiniteQueryFunctionContext<int>, Task<string>> PageFn => ctx =>
    {
        _fetchCount++;
        return Task.FromResult($"page-{ctx.PageParam}");
    };

    private static InfiniteData<string, int> CachedPage(string page = "old") => new() { Pages = [page], PageParams = [0] };

    #region ensureQueryData

    // Ports: queryClient.test.tsx > queryClient > ensureQueryData > should return the cached query data if the query is found
    [Fact]
    public async Task EnsureQueryDataAsync_CachedData_ReturnsItEvenIfStale()
    {
        // Arrange
        _client.SetQueryData(Key, "bar");

        // Act
        var result = await _client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
        });

        // Assert
        Assert.Equal("bar", result);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureQueryData > should return the cached query data if the query is found and cached query data is falsy
    [Fact]
    public async Task EnsureQueryDataAsync_CachedZero_ReturnsZero()
    {
        // Arrange
        _client.SetQueryData(Key, 0);

        // Act
        var result = await _client.EnsureQueryDataAsync(new FetchQueryOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(1),
        });

        // Assert
        Assert.Equal(0, result);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureQueryData > should call ensureQueryData and return its results if the query is not found
    [Fact]
    public async Task EnsureQueryDataAsync_NoCachedData_Fetches()
    {
        // Act
        var result = await _client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
        });

        // Assert
        Assert.Equal("data", result);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureQueryData > should return the cached query data if the query is found and preFetchQuery in the background when revalidateIfStale is set
    [Fact]
    public async Task EnsureQueryDataAsync_RevalidateIfStale_ReturnsStaleDataThenUpdatesCache()
    {
        // Arrange
        _client.SetQueryData(Key, "old");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _client.QueryCache.Subscribe(_ =>
        {
            if (_client.GetQueryData<string>(Key) is "new")
            {
                settled.TrySetResult();
            }
        });
        var options = new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = _ => release.Task,
            RevalidateIfStale = true,
        };

        // Act
        var first = await _client.EnsureQueryDataAsync(options);
        release.SetResult("new");
        await settled.Task;
        var second = await _client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("unused"),
        });

        // Assert
        Assert.Equal("old", first);
        Assert.Equal("new", second);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureQueryData > should not fetch with initialData
    [Fact]
    public async Task EnsureQueryDataAsync_InitialData_ReturnsItWithoutFetching()
    {
        // Act
        var result = await _client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            InitialData = "initial",
        });

        // Assert
        Assert.Equal("initial", result);
        Assert.Equal(0, _fetchCount);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_ValueTypeWithoutInitialData_Fetches()
    {
        // Act
        var result = await _client.EnsureQueryDataAsync(new FetchQueryOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(7),
        });

        // Assert
        Assert.Equal(7, result);
        Assert.Equal(1, _fetchCount);
    }

    #endregion

    #region ensureInfiniteQueryData

    // Ports: queryClient.test.tsx > queryClient > ensureInfiniteQueryData > should return the cached query data if the query is found
    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_CachedData_ReturnsItEvenIfStale()
    {
        // Arrange
        _client.SetQueryData(Key, CachedPage("bar"));

        // Act
        var result = await _client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 1,
        });

        // Assert
        Assert.Equal(["bar"], result.Pages);
        Assert.Equal([0], result.PageParams);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureInfiniteQueryData > should fetch the query and return its results if the query is not found
    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_NoCachedData_Fetches()
    {
        // Act
        var result = await _client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 1,
        });

        // Assert
        Assert.Equal(["page-1"], result.Pages);
        Assert.Equal([1], result.PageParams);
    }

    // Ports: queryClient.test.tsx > queryClient > ensureInfiniteQueryData > should return the cached query data if the query is found and preFetchQuery in the background when revalidateIfStale is set
    [Fact]
    public async Task EnsureInfiniteQueryDataAsync_RevalidateIfStale_ReturnsStaleDataThenUpdatesCache()
    {
        // Arrange
        _client.SetQueryData(Key, CachedPage());
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _client.QueryCache.Subscribe(_ =>
        {
            if (_client.GetQueryData<InfiniteData<string, int>>(Key).Pages[0] is "new")
            {
                settled.TrySetResult();
            }
        });
        var options = new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = _ => release.Task,
            InitialPageParam = 1,
            RevalidateIfStale = true,
        };

        // Act
        var first = await _client.EnsureInfiniteQueryDataAsync(options);
        release.SetResult("new");
        await settled.Task;
        var second = await _client.EnsureInfiniteQueryDataAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 1,
        });

        // Assert
        Assert.Equal(["old"], first.Pages);
        Assert.Equal(["new"], second.Pages);
        Assert.Equal([0], second.PageParams);
    }

    #endregion

    #region fetchQuery

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should not retry by default
    [Fact]
    public async Task FetchQueryAsync_FetchFails_DoesNotRetryByDefault()
    {
        // Arrange
        var attempts = 0;

        // Act
        Task Act() => _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = _ =>
            {
                attempts++;
                throw new InvalidDataException("error");
            },
        });

        // Assert
        await Assert.ThrowsAsync<InvalidDataException>(Act);
        Assert.Equal(1, attempts);
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should return the cached data on cache hit
    [Fact]
    public async Task FetchQueryAsync_FreshCachedData_ReturnsSameInstance()
    {
        // Arrange
        var options = new FetchQueryOptions<object>
        {
            QueryKey = Key,
            QueryFn = Returning(new object()),
            StaleTime = TimeSpan.FromMinutes(1),
        };

        // Act
        var first = await _client.FetchQueryAsync(options);
        var second = await _client.FetchQueryAsync(options);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should read from cache with static staleTime even if invalidated
    [Fact]
    public async Task FetchQueryAsync_StaticStaleTimeAfterInvalidation_ReturnsCachedData()
    {
        // Arrange
        var options = new FetchQueryOptions<object>
        {
            QueryKey = Key,
            QueryFn = Returning(new object()),
            StaleTime = Timeout.InfiniteTimeSpan,
        };
        var first = await _client.FetchQueryAsync(options);
        await _client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            QueryKey = Key,
            RefetchType = InvalidateRefetchType.None,
        });

        // Act
        var second = await _client.FetchQueryAsync(options);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should be able to fetch when garbage collection time is set to 0 and then be removed
    [Fact]
    public async Task FetchQueryAsync_ZeroGcTime_ReturnsDataThenRemovesQuery()
    {
        // Act
        var result = await _client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(1),
            GcTime = TimeSpan.Zero,
        });
        _time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.Equal(1, result);
        Assert.Null(_client.QueryCache.Get<int>(DefaultQueryKeyHasher.Instance.HashQueryKey(Key)));
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should keep a query in cache if garbage collection time is Infinity
    [Fact]
    public async Task FetchQueryAsync_InfiniteGcTime_KeepsQuery()
    {
        // Act
        await _client.FetchQueryAsync(new FetchQueryOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(1),
            GcTime = Timeout.InfiniteTimeSpan,
        });
        _time.Advance(TimeSpan.FromDays(1));

        // Assert
        Assert.Equal(1, _client.GetQueryData<int>(Key));
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should not force fetch
    [Fact]
    public async Task FetchQueryAsync_FreshCachedDataAndInitialData_PrefersCachedData()
    {
        // Arrange
        _client.SetQueryData(Key, "og");

        // Act
        var result = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("new"),
            InitialData = "initial",
            StaleTime = TimeSpan.FromMilliseconds(100),
        });

        // Assert
        Assert.Equal("og", result);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > fetchQuery > should only fetch if the data is older then the given stale time
    [Fact]
    public async Task FetchQueryAsync_DataOlderThanStaleTime_Refetches()
    {
        // Arrange
        var count = 0;
        Func<QueryFunctionContext, Task<int>> queryFn = _ => Task.FromResult(++count);
        _client.SetQueryData(Key, count);
        FetchQueryOptions<int> WithStaleTime(int milliseconds) => new()
        {
            QueryKey = Key,
            QueryFn = queryFn,
            StaleTime = TimeSpan.FromMilliseconds(milliseconds),
        };

        // Act
        var first = await _client.FetchQueryAsync(WithStaleTime(100));
        _time.Advance(TimeSpan.FromMilliseconds(10));
        var second = await _client.FetchQueryAsync(WithStaleTime(10));
        var third = await _client.FetchQueryAsync(WithStaleTime(10));
        _time.Advance(TimeSpan.FromMilliseconds(10));
        var fourth = await _client.FetchQueryAsync(WithStaleTime(10));

        // Assert
        Assert.Equal([0, 1, 1, 2], [first, second, third, fourth]);
    }

    // queryClient.ts fetchQuery; query.ts Query#fetch.
    [Fact]
    public async Task FetchQueryAsync_CacheHit_KeepsExistingQueryFn()
    {
        // Arrange
        await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("original"),
        });
        await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = SkipToken.QueryFn<string>(),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Act
        await _client.RefetchQueriesAsync(Key);

        // Assert
        Assert.Equal(2, _fetchCount);
    }

    // utils.ts ensureQueryFn.
    [Fact]
    public async Task FetchQueryAsync_SkipTokenWithoutCachedData_ThrowsMissingQueryFn()
    {
        // Act
        Task Act() => _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = SkipToken.QueryFn<string>(),
        });

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Contains("Missing queryFn", exception.Message);
    }

    // query.ts Query#isStaleByTime.
    [Fact]
    public async Task FetchQueryAsync_StaticStaleTimeAfterFailedRefetch_ReturnsKeptData()
    {
        // Arrange
        _client.SetQueryData(Key, "cached");

        async Task Refetch() => await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = _ => Task.FromException<string>(new InvalidDataException()),
        });
        await Assert.ThrowsAsync<InvalidDataException>(Refetch);

        // Act
        var result = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("fetched"),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal("cached", result);
        Assert.Equal(0, _fetchCount);
    }

    // queryClient.ts fetchQuery.
    [Fact]
    public async Task FetchQueryAsync_StaticStaleTimeWithInitialData_ReturnsInitialData()
    {
        // Act
        var result = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            StaleTime = Timeout.InfiniteTimeSpan,
            InitialData = "initial",
        });

        // Assert
        Assert.Equal("initial", result);
        Assert.Equal(0, _fetchCount);
    }

    #endregion

    #region fetchInfiniteQuery

    // Ports: queryClient.test.tsx > queryClient > fetchInfiniteQuery > should return infinite query data
    [Fact]
    public async Task FetchInfiniteQueryAsync_NoGetNextPageParam_ReturnsAndCachesFirstPage()
    {
        // Act
        var result = await _client.FetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 10,
        });

        // Assert
        Assert.Equal(["page-10"], result.Pages);
        Assert.Equal([10], result.PageParams);
        Assert.Same(result, _client.GetQueryData<InfiniteData<string, int>>(Key));
    }

    #endregion

    #region prefetchInfiniteQuery

    // Ports: queryClient.test.tsx > queryClient > prefetchInfiniteQuery > should return infinite query data
    [Fact]
    public async Task PrefetchInfiniteQueryAsync_CachesFirstPage()
    {
        // Act
        await _client.PrefetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 10,
        });
        var cached = _client.GetQueryData<InfiniteData<string, int>>(Key);

        // Assert
        Assert.Equal(["page-10"], cached.Pages);
        Assert.Equal([10], cached.PageParams);
    }

    // Ports: queryClient.test.tsx > queryClient > prefetchInfiniteQuery > should prefetch multiple pages
    [Fact]
    public async Task PrefetchInfiniteQueryAsync_Pages_FetchesThatManyPages()
    {
        // Act
        await _client.PrefetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            GetNextPageParam = ctx => PageParamResult<int>.Some(ctx.PageParam + 5),
            InitialPageParam = 10,
            Pages = 3,
        });
        var cached = _client.GetQueryData<InfiniteData<string, int>>(Key);

        // Assert
        Assert.Equal(["page-10", "page-15", "page-20"], cached.Pages);
        Assert.Equal([10, 15, 20], cached.PageParams);
    }

    // Ports: queryClient.test.tsx > queryClient > prefetchInfiniteQuery > should stop prefetching if getNextPageParam returns undefined
    [Fact]
    public async Task PrefetchInfiniteQueryAsync_PagesWhenNextPageParamIsNone_StopsEarly()
    {
        // Arrange
        var evaluations = 0;

        // Act
        await _client.PrefetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            GetNextPageParam = ctx =>
            {
                evaluations++;
                return ctx.PageParam >= 20 ? PageParamResult<int>.None : PageParamResult<int>.Some(ctx.PageParam + 5);
            },
            InitialPageParam = 10,
            Pages = 5,
        });
        var cached = _client.GetQueryData<InfiniteData<string, int>>(Key);

        // Assert
        Assert.Equal(["page-10", "page-15", "page-20"], cached.Pages);
        Assert.Equal(3, evaluations);
    }

    #endregion

    #region prefetchQuery

    // Ports: queryClient.test.tsx > queryClient > prefetchQuery > should return undefined when an error is thrown
    [Fact]
    public async Task PrefetchQueryAsync_FetchFails_CompletesWithoutThrowing()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => _client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = _ => Task.FromException<string>(new InvalidDataException()),
            Retry = 0,
        }));

        // Assert
        Assert.Null(exception);
    }

    // Ports: queryClient.test.tsx > queryClient > prefetchQuery > should be garbage collected after gcTime if unused
    [Fact]
    public async Task PrefetchQueryAsync_GcTimeElapsedWithoutObservers_RemovesQuery()
    {
        // Arrange
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(Key);

        // Act
        await _client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            GcTime = TimeSpan.FromMilliseconds(10),
        });
        var beforeGc = _client.QueryCache.Get<string>(hash)?.State?.Data;
        _time.Advance(TimeSpan.FromMilliseconds(15));

        // Assert
        Assert.Equal("data", beforeGc);
        Assert.Null(_client.QueryCache.Get<string>(hash));
    }

    #endregion
}
