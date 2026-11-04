using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

public sealed class QueryExecuteTests
{
    private static readonly QueryKey Key = ["key"];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly QueryClient _client;
    private int _fetchCount;

    public QueryExecuteTests()
    {
        _client = new QueryClient(new QueryCache(), timeProvider: _time);
    }

    private Func<QueryFunctionContext, Task<T>> Returning<T>(T value) => _ =>
    {
        _fetchCount++;
        return Task.FromResult(value);
    };

    private static Func<QueryFunctionContext, Task<string>> Throwing(Exception exception)
        => _ => Task.FromException<string>(exception);

    private Func<InfiniteQueryFunctionContext<int>, Task<string>> PageFn => ctx =>
    {
        _fetchCount++;
        return Task.FromResult($"page-{ctx.PageParam}");
    };

    private static InfiniteData<string, int> CachedPage() => new() { Pages = ["cached"], PageParams = [0] };

    #region query with static staleTime

    // Ports: queryClient.test.tsx > queryClient > query with static staleTime > should return the cached query data if the query is found
    [Fact]
    public async Task QueryAsync_StaticStaleTimeWithCachedData_ReturnsCachedData()
    {
        // Arrange
        _client.SetQueryData(Key, "bar");

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal("bar", result);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query with static staleTime > should return the cached query data if the query is found and cached query data is falsy
    [Fact]
    public async Task QueryAsync_StaticStaleTimeWithCachedZero_ReturnsZero()
    {
        // Arrange
        _client.SetQueryData(Key, 0);

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(1),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal(0, result);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query with static staleTime > should call queryFn and return its results if the query is not found
    [Fact]
    public async Task QueryAsync_StaticStaleTimeWithoutCachedData_Fetches()
    {
        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal("data", result);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query with static staleTime > should not fetch when initialData is provided
    [Fact]
    public async Task QueryAsync_StaticStaleTimeWithInitialData_ReturnsInitialData()
    {
        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
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

    // Ports: queryClient.test.tsx > queryClient > query with static staleTime > should support manual background revalidation via a second query call
    [Fact]
    public async Task QueryAsync_ZeroStaleTimeAfterStaticCall_RefetchesAndUpdatesCache()
    {
        // Arrange
        await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data-1"),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Act
        await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data-2"),
            StaleTime = TimeSpan.Zero,
        });

        // Assert
        Assert.Equal(2, _fetchCount);
        Assert.Equal("data-2", _client.GetQueryData<string>(Key));
    }

    #endregion

    #region query

    // Ports: queryClient.test.tsx > queryClient > query > should not retry by default
    [Fact]
    public async Task QueryAsync_FetchFails_DoesNotRetryByDefault()
    {
        // Arrange
        var attempts = 0;

        // Act
        Task Act() => _client.QueryAsync(new QueryExecuteOptions<string>
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

    // Ports: queryClient.test.tsx > queryClient > query > should return the cached data on cache hit
    [Fact]
    public async Task QueryAsync_FreshCachedData_ReturnsSameInstance()
    {
        // Arrange
        var options = new QueryExecuteOptions<object>
        {
            QueryKey = Key,
            QueryFn = Returning(new object()),
            StaleTime = TimeSpan.FromMinutes(1),
        };

        // Act
        var first = await _client.QueryAsync(options);
        var second = await _client.QueryAsync(options);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should throw when skipToken is provided and no cached data exists
    [Fact]
    public async Task QueryAsync_SkipTokenWithoutCachedData_ThrowsWithoutSelecting()
    {
        // Arrange
        var selected = false;

        // Act
        Task Act() => _client.QueryAsync(
            new QueryExecuteOptions<string>
            {
                QueryKey = Key,
                QueryFn = SkipToken.QueryFn<string>(),
            },
            data =>
            {
                selected = true;
                return data.Length;
            });

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Contains("Missing queryFn", exception.Message);
        Assert.False(selected);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should throw when skipToken is provided with no cached data
    [Fact]
    public async Task QueryAsync_SkipTokenWithoutCachedData_Throws()
    {
        // Act
        Task Act() => _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = SkipToken.QueryFn<string>(),
        });

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Contains("Missing queryFn", exception.Message);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should return cached data when skipToken is provided
    [Fact]
    public async Task QueryAsync_SkipTokenWithFreshCachedData_SelectsFromCache()
    {
        // Arrange
        _client.SetQueryData(Key, "cached-data");

        // Act
        var result = await _client.QueryAsync(
            new QueryExecuteOptions<string>
            {
                QueryKey = Key,
                QueryFn = SkipToken.QueryFn<string>(),
                StaleTime = Timeout.InfiniteTimeSpan,
            },
            data => data.Length);

        // Assert
        Assert.Equal("cached-data".Length, result);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should read from cache with static staleTime even if invalidated
    [Fact]
    public async Task QueryAsync_StaticStaleTimeAfterInvalidation_ReturnsCachedData()
    {
        // Arrange
        var options = new QueryExecuteOptions<object>
        {
            QueryKey = Key,
            QueryFn = Returning(new object()),
            StaleTime = Timeout.InfiniteTimeSpan,
        };
        var first = await _client.QueryAsync(options);
        await _client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            QueryKey = Key,
            RefetchType = InvalidateRefetchType.None,
        });

        // Act
        var second = await _client.QueryAsync(options);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should be able to fetch when garbage collection time is set to 0 and then be removed
    [Fact]
    public async Task QueryAsync_ZeroGcTime_ReturnsDataThenRemovesQuery()
    {
        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<int>
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

    // Ports: queryClient.test.tsx > queryClient > query > should keep a query in cache if garbage collection time is Infinity
    [Fact]
    public async Task QueryAsync_InfiniteGcTime_KeepsQuery()
    {
        // Act
        await _client.QueryAsync(new QueryExecuteOptions<int>
        {
            QueryKey = Key,
            QueryFn = Returning(1),
            GcTime = Timeout.InfiniteTimeSpan,
        });
        _time.Advance(TimeSpan.FromDays(1));

        // Assert
        Assert.Equal(1, _client.GetQueryData<int>(Key));
    }

    // Ports: queryClient.test.tsx > queryClient > query > should not force fetch
    [Fact]
    public async Task QueryAsync_FreshCachedDataAndInitialData_PrefersCachedData()
    {
        // Arrange
        _client.SetQueryData(Key, "og");

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
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

    // Ports: queryClient.test.tsx > queryClient > query > should only fetch if the data is older then the given stale time
    [Fact]
    public async Task QueryAsync_DataOlderThanStaleTime_Refetches()
    {
        // Arrange
        var count = 0;
        Func<QueryFunctionContext, Task<int>> queryFn = _ => Task.FromResult(++count);
        _client.SetQueryData(Key, count);
        QueryExecuteOptions<int> WithStaleTime(int milliseconds) => new()
        {
            QueryKey = Key,
            QueryFn = queryFn,
            StaleTime = TimeSpan.FromMilliseconds(milliseconds),
        };

        // Act
        var first = await _client.QueryAsync(WithStaleTime(100));
        _time.Advance(TimeSpan.FromMilliseconds(10));
        var second = await _client.QueryAsync(WithStaleTime(10));
        var third = await _client.QueryAsync(WithStaleTime(10));
        _time.Advance(TimeSpan.FromMilliseconds(10));
        var fourth = await _client.QueryAsync(WithStaleTime(10));

        // Assert
        Assert.Equal([0, 1, 1, 2], [first, second, third, fourth]);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should evaluate staleTime when provided as a function
    [Fact]
    public async Task QueryAsync_StaleTimeFn_EvaluatedOnceAndPreferredOverStaleTime()
    {
        // Arrange
        var evaluations = 0;
        _client.SetQueryData(Key, "old-data");
        _time.Advance(TimeSpan.FromMilliseconds(1));

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("new-data"),
            StaleTime = Timeout.InfiniteTimeSpan,
            StaleTimeFn = _ =>
            {
                evaluations++;
                return TimeSpan.Zero;
            },
        });

        // Assert
        Assert.Equal("new-data", result);
        Assert.Equal(1, _fetchCount);
        Assert.Equal(1, evaluations);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should propagate errors
    [Fact]
    public async Task QueryAsync_FetchFails_PropagatesException()
    {
        // Arrange
        var expected = new InvalidDataException("error");

        // Act
        Task Act() => _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Throwing(expected),
        });

        // Assert
        var actual = await Assert.ThrowsAsync<InvalidDataException>(Act);
        Assert.Same(expected, actual);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should apply select when data is fresh in cache
    [Fact]
    public async Task QueryAsync_SelectWithFreshCachedData_SelectsFromCacheWithoutChangingIt()
    {
        // Arrange
        _client.SetQueryData(Key, "cached-data");

        // Act
        var result = await _client.QueryAsync(
            new QueryExecuteOptions<string>
            {
                QueryKey = Key,
                QueryFn = Returning("fetched-data"),
                StaleTime = Timeout.InfiniteTimeSpan,
            },
            data => $"{data}-selected");

        // Assert
        Assert.Equal("cached-data-selected", result);
        Assert.Equal("cached-data", _client.GetQueryData<string>(Key));
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should apply select to freshly fetched data
    [Fact]
    public async Task QueryAsync_SelectWithFetch_SelectsFromFetchedData()
    {
        // Act
        var result = await _client.QueryAsync(
            new QueryExecuteOptions<string>
            {
                QueryKey = Key,
                QueryFn = Returning("fetched-data"),
            },
            data => data.ToUpperInvariant());

        // Assert
        Assert.Equal("FETCHED-DATA", result);
        Assert.Equal(1, _fetchCount);
    }

    #endregion

    #region meta

    // Ports: query.test.tsx > query > should provide meta object inside query function
    [Fact]
    public async Task QueryAsync_Meta_ProvidedToQueryFn()
    {
        // Arrange
        var meta = new Meta(new Dictionary<string, object?> { ["it"] = "works" });

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<Meta?>
        {
            QueryKey = Key,
            QueryFn = ctx => Task.FromResult(ctx.Meta),
            Meta = meta,
        });

        // Assert
        Assert.Same(meta, result);
    }

    // Ports: queryClient.test.tsx > queryClient > query > should allow new meta
    [Fact]
    public async Task QueryAsync_NewMetaOnRefetch_ProvidedToQueryFn()
    {
        // Arrange
        QueryExecuteOptions<Meta?> WithMeta(Meta meta) => new()
        {
            QueryKey = Key,
            QueryFn = ctx => Task.FromResult(ctx.Meta),
            Meta = meta,
        };
        var firstMeta = new Meta(new Dictionary<string, object?> { ["foo"] = true });
        var secondMeta = new Meta(new Dictionary<string, object?> { ["foo"] = false });

        // Act
        var first = await _client.QueryAsync(WithMeta(firstMeta));
        var second = await _client.QueryAsync(WithMeta(secondMeta));

        // Assert
        Assert.Same(firstMeta, first);
        Assert.Same(secondMeta, second);
    }

    [Fact]
    public async Task QueryAsync_NoMeta_ProvidesNull()
    {
        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<Meta?>
        {
            QueryKey = Key,
            QueryFn = ctx => Task.FromResult(ctx.Meta),
        });

        // Assert
        Assert.Null(result);
    }

    // infiniteQueryBehavior.ts infiniteQueryBehavior.
    [Fact]
    public async Task InfiniteQueryAsync_Meta_ProvidedToEveryPage()
    {
        // Arrange
        var meta = new Meta();
        var seen = new List<Meta?>();

        // Act
        await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<int, int>
        {
            QueryKey = Key,
            QueryFn = ctx =>
            {
                seen.Add(ctx.Meta);
                return Task.FromResult(ctx.PageParam);
            },
            InitialPageParam = 0,
            GetNextPageParam = ctx => PageParamResult<int>.Some(ctx.PageParam + 1),
            Pages = 2,
            Meta = meta,
        });

        // Assert
        Assert.Equal(2, seen.Count);
        Assert.All(seen, actual => Assert.Same(meta, actual));
    }

    // Ports: infiniteQueryObserver.test.tsx > InfiniteQueryObserver > should pass the meta option to the queryFn
    [Fact]
    public async Task InfiniteQueryObserver_Meta_ProvidedToQueryFn()
    {
        // Arrange
        var meta = new Meta(new Dictionary<string, object?> { ["it"] = "works" });
        var received = new TaskCompletionSource<Meta?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new InfiniteQueryObserver<int, int>(
            _client,
            new InfiniteQueryObserverOptions<int, int>
            {
                QueryKey = Key,
                QueryFn = ctx =>
                {
                    received.TrySetResult(ctx.Meta);
                    return Task.FromResult(1);
                },
                InitialPageParam = 1,
                GetNextPageParam = _ => PageParamResult<int>.Some(2),
                Meta = meta,
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });
        var actual = await received.Task;

        // Assert
        Assert.Same(meta, actual);
    }

    #endregion

    #region query used for prefetching

    // Ports: queryClient.test.tsx > queryClient > query used for prefetching > should be garbage collected after gcTime if unused
    [Fact]
    public async Task QueryAsync_GcTimeElapsedWithoutObservers_RemovesQuery()
    {
        // Arrange
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(Key);

        // Act
        await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("data"),
            GcTime = TimeSpan.FromMilliseconds(10),
        });
        var beforeGc = _client.QueryCache.Get<string>(hash);
        _time.Advance(TimeSpan.FromMilliseconds(15));

        // Assert
        Assert.NotNull(beforeGc);
        Assert.Null(_client.QueryCache.Get<string>(hash));
    }

    #endregion

    #region Staleness that upstream's disabled tests also cover

    // queryClient.test.tsx 'should fetch instead of returning initialData when disabled'.
    [Fact]
    public async Task QueryAsync_StaleInitialData_FetchesAndReturnsFetchedData()
    {
        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("fetched-data"),
            InitialData = "initial-data",
        });

        // Assert
        Assert.Equal("fetched-data", result);
        Assert.Equal(1, _fetchCount);
    }

    // queryClient.test.tsx 'should fetch when disabled and apply select'.
    [Fact]
    public async Task QueryAsync_SelectWithStaleCachedData_SelectsFromFetchedData()
    {
        // Arrange
        _client.SetQueryData(Key, "cached-data");

        // Act
        var result = await _client.QueryAsync(
            new QueryExecuteOptions<string>
            {
                QueryKey = Key,
                QueryFn = Returning("fetched-data"),
                StaleTime = TimeSpan.Zero,
            },
            data => $"{data}-selected");

        // Assert
        Assert.Equal("fetched-data-selected", result);
        Assert.Equal(1, _fetchCount);
    }

    // queryClient.test.tsx 'should fetch when disabled and apply select'.
    [Fact]
    public async Task InfiniteQueryAsync_SelectWithStaleCachedData_SelectsFromFetchedData()
    {
        // Arrange
        _client.SetQueryData(Key, CachedPage());

        // Act
        var result = await _client.InfiniteQueryAsync(
            new InfiniteQueryExecuteOptions<string, int>
            {
                QueryKey = Key,
                QueryFn = PageFn,
                InitialPageParam = 0,
            },
            data => data.Pages.Select(page => $"{page}-selected").ToList());

        // Assert
        Assert.Equal(["page-0-selected"], result);
        Assert.Equal(1, _fetchCount);
    }

    // query.ts Query#fetch; queryClient.ts QueryClient#query.
    [Fact]
    public async Task QueryAsync_DisabledObserverOnSameKey_FetchesWithItsOwnQueryFn()
    {
        // Arrange
        var observerFetches = 0;
        var observer = new QueryObserver<string, string>(
            _client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = Key,
                QueryFn = _ =>
                {
                    observerFetches++;
                    return Task.FromResult("observer-data");
                },
                Enabled = false,
            });
        using var subscription = observer.Subscribe(_ => { });

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("imperative-data"),
        });

        // Assert
        Assert.Equal("imperative-data", result);
        Assert.Equal(1, _fetchCount);
        Assert.Equal(0, observerFetches);
    }

    #endregion

    #region Behaviour specific to the C# port

    // query.ts Query#isStaleByTime.
    [Theory]
    [InlineData(true, "cached", 0)]
    [InlineData(false, "fetched", 1)]
    public async Task QueryAsync_DataKeptAfterFailedRefetch_ServedOnlyWithStaticStaleTime(
        bool isStatic,
        string expected,
        int expectedFetchCount)
    {
        // Arrange
        _client.SetQueryData(Key, "cached");

        async Task Refetch() => await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Throwing(new InvalidDataException()),
        });
        await Assert.ThrowsAsync<InvalidDataException>(Refetch);

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("fetched"),
            StaleTime = isStatic ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(1),
        });

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(expectedFetchCount, _fetchCount);
    }

    // query.ts Query#isStaleByTime.
    [Fact]
    public async Task QueryAsync_StaticStaleTimeAfterFailedFirstFetch_Fetches()
    {
        // Arrange
        async Task FailedFetch() => await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Throwing(new InvalidDataException()),
        });
        await Assert.ThrowsAsync<InvalidDataException>(FailedFetch);

        // Act
        var result = await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("fetched"),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal("fetched", result);
    }

    // query.ts Query#fetch.
    [Fact]
    public async Task QueryAsync_CacheHit_KeepsExistingQueryFn()
    {
        // Arrange
        await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = Returning("original"),
        });
        await _client.QueryAsync(new QueryExecuteOptions<string>
        {
            QueryKey = Key,
            QueryFn = SkipToken.QueryFn<string>(),
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Act
        await _client.RefetchQueriesAsync(Key);

        // Assert
        Assert.Equal(2, _fetchCount);
        Assert.Equal("original", _client.GetQueryData<string>(Key));
    }

    [Fact]
    public async Task QueryAsync_QueryOptionsDefinition_ExecutesAndSelects()
    {
        // Arrange
        var definition = QueryOptions.Create(Key, Returning("data"));

        // Act
        var data = await _client.QueryAsync(definition);
        var length = await _client.QueryAsync(definition, value => value.Length);

        // Assert
        Assert.Equal("data", data);
        Assert.Equal(4, length);
    }

    #endregion

    #region infiniteQuery with static staleTime

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery with static staleTime > should return the cached query data if the query is found
    [Fact]
    public async Task InfiniteQueryAsync_StaticStaleTimeWithCachedData_ReturnsCachedData()
    {
        // Arrange
        _client.SetQueryData(Key, CachedPage());

        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 1,
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal(["cached"], result.Pages);
        Assert.Equal([0], result.PageParams);
        Assert.Equal(0, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery with static staleTime > should fetch the query and return its results if the query is not found
    [Fact]
    public async Task InfiniteQueryAsync_StaticStaleTimeWithoutCachedData_Fetches()
    {
        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 1,
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal(["page-1"], result.Pages);
        Assert.Equal([1], result.PageParams);
        Assert.Equal(1, _fetchCount);
    }

    #endregion

    #region infiniteQuery

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should return infinite query data
    [Fact]
    public async Task InfiniteQueryAsync_NoCachedData_ReturnsAndCachesFirstPage()
    {
        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 10,
        });
        var cached = _client.GetQueryData<InfiniteData<string, int>>(Key);

        // Assert
        Assert.Equal(["page-10"], result.Pages);
        Assert.Equal([10], result.PageParams);
        Assert.Same(result, cached);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should return cached data when skipToken is provided
    [Fact]
    public async Task InfiniteQueryAsync_SkipTokenWithFreshCachedData_ReturnsCachedData()
    {
        // Arrange
        _client.SetQueryData(Key, CachedPage());

        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = SkipToken.InfiniteQueryFn<string, int>(),
            InitialPageParam = 0,
            StaleTime = Timeout.InfiniteTimeSpan,
        });

        // Assert
        Assert.Equal(["cached"], result.Pages);
        Assert.Equal([0], result.PageParams);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should throw when skipToken is provided and no cached data exists
    [Fact]
    public async Task InfiniteQueryAsync_SkipTokenWithoutCachedData_ThrowsWithoutSelecting()
    {
        // Arrange
        var selected = false;

        // Act
        Task Act() => _client.InfiniteQueryAsync(
            new InfiniteQueryExecuteOptions<string, int>
            {
                QueryKey = Key,
                QueryFn = SkipToken.InfiniteQueryFn<string, int>(),
                InitialPageParam = 0,
            },
            data =>
            {
                selected = true;
                return data.Pages.Count;
            });

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Contains("Missing queryFn", exception.Message);
        Assert.False(selected);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should throw when skipToken is provided with no cached data
    [Fact]
    public async Task InfiniteQueryAsync_SkipTokenWithoutCachedData_Throws()
    {
        // Act
        Task Act() => _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = SkipToken.InfiniteQueryFn<string, int>(),
            InitialPageParam = 0,
        });

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Contains("Missing queryFn", exception.Message);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should evaluate staleTime callback and refetch when it returns stale
    [Fact]
    public async Task InfiniteQueryAsync_StaleTimeFnReturnsZero_Refetches()
    {
        // Arrange
        var evaluations = 0;
        _client.SetQueryData(Key, CachedPage());
        _time.Advance(TimeSpan.FromMilliseconds(1));

        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 0,
            StaleTimeFn = query =>
            {
                evaluations++;
                Assert.Equal("cached", query.State?.Data?.Pages[0]);
                return TimeSpan.Zero;
            },
        });

        // Assert
        Assert.Equal(["page-0"], result.Pages);
        Assert.Equal([0], result.PageParams);
        Assert.Equal(1, evaluations);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should read from cache with static staleTime even if invalidated
    [Fact]
    public async Task InfiniteQueryAsync_StaticStaleTimeAfterInvalidation_ReturnsCachedData()
    {
        // Arrange
        var options = new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 0,
            StaleTime = Timeout.InfiniteTimeSpan,
        };
        var first = await _client.InfiniteQueryAsync(options);
        await _client.InvalidateQueriesAsync(new InvalidateQueryFilters
        {
            QueryKey = Key,
            RefetchType = InvalidateRefetchType.None,
        });

        // Act
        var second = await _client.InfiniteQueryAsync(options);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, _fetchCount);
    }

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery > should apply select to infinite query data
    [Fact]
    public async Task InfiniteQueryAsync_Select_TransformsPages()
    {
        // Act
        var result = await _client.InfiniteQueryAsync(
            new InfiniteQueryExecuteOptions<int, int>
            {
                QueryKey = Key,
                QueryFn = ctx => Task.FromResult(ctx.PageParam),
                InitialPageParam = 10,
            },
            data => data.Pages.Select(page => page * 2).ToList());

        // Assert
        Assert.Equal([20], result);
    }

    #endregion

    #region infiniteQuery used for prefetching

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery used for prefetching > should return infinite query data
    [Fact]
    public async Task InfiniteQueryAsync_ForPrefetching_CachesFirstPage()
    {
        // Act
        await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
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

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery used for prefetching > should prefetch multiple pages
    [Fact]
    public async Task InfiniteQueryAsync_Pages_FetchesThatManyPages()
    {
        // Act
        await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
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

    // Ports: queryClient.test.tsx > queryClient > infiniteQuery used for prefetching > should stop prefetching if getNextPageParam returns undefined
    [Fact]
    public async Task InfiniteQueryAsync_PagesWhenNextPageParamIsNone_StopsEarly()
    {
        // Arrange
        var evaluations = 0;

        // Act
        await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
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
        Assert.Equal([10, 15, 20], cached.PageParams);
        Assert.Equal(3, evaluations);
    }

    #endregion

    #region Infinite behaviour specific to the C# port

    // types.ts InfiniteQueryPages.
    [Fact]
    public async Task InfiniteQueryAsync_PagesWithoutGetNextPageParam_Throws()
    {
        // Act
        Task Act() => _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            InitialPageParam = 0,
            Pages = 2,
        });

        // Assert
        await Assert.ThrowsAsync<ArgumentException>("options", Act);
        Assert.Equal(0, _fetchCount);
    }

    // infiniteQueryBehavior.ts infiniteQueryBehavior.
    [Fact]
    public async Task InfiniteQueryAsync_PagesAboveMaxPages_KeepsLastPages()
    {
        // Act
        var result = await _client.InfiniteQueryAsync(new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            GetNextPageParam = ctx => PageParamResult<int>.Some(ctx.PageParam + 1),
            InitialPageParam = 0,
            Pages = 4,
            MaxPages = 2,
        });

        // Assert
        Assert.Equal(["page-2", "page-3"], result.Pages);
        Assert.Equal([2, 3], result.PageParams);
        Assert.Equal(4, _fetchCount);
    }

    // infiniteQueryBehavior.ts infiniteQueryBehavior.
    [Fact]
    public async Task InfiniteQueryAsync_PagesBelowCachedCount_RefetchesFewerPages()
    {
        // Arrange
        var options = new InfiniteQueryExecuteOptions<string, int>
        {
            QueryKey = Key,
            QueryFn = PageFn,
            GetNextPageParam = ctx => PageParamResult<int>.Some(ctx.PageParam + 1),
            InitialPageParam = 0,
        };
        await _client.InfiniteQueryAsync(options with { Pages = 3 });

        // Act
        var result = await _client.InfiniteQueryAsync(options with { Pages = 1 });

        // Assert
        Assert.Equal(["page-0"], result.Pages);
        Assert.Equal([0], result.PageParams);
    }

    // queryClient.ts defaultQueryOptions.
    [Fact]
    public async Task InfiniteQueryObserver_SkipToken_DoesNotFetch()
    {
        // Arrange
        var observer = new InfiniteQueryObserver<string, int>(
            _client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = Key,
                QueryFn = SkipToken.InfiniteQueryFn<string, int>(),
                InitialPageParam = 0,
                GetNextPageParam = _ => PageParamResult<int>.None,
            });

        // Act
        using var subscription = observer.Subscribe(_ => { });
        await Task.Yield();

        // Assert
        Assert.Equal(FetchStatus.Idle, observer.CurrentResult.FetchStatus);
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);
    }

    #endregion
}
