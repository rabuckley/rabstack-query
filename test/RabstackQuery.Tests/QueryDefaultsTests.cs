using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

public sealed class QueryDefaultsTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FocusManager _focusManager = new();
    private readonly OnlineManager _onlineManager = new();
    private readonly QueryClient _client;

    public QueryDefaultsTests()
    {
        _client = new QueryClient(
            new QueryCache(),
            timeProvider: _time,
            focusManager: _focusManager,
            onlineManager: _onlineManager);
    }

    public void Dispose() => _client.Dispose();


    [Fact]
    public async Task ClientDefaultStaleTime_MakesObserverDataFresh()
    {
        // Arrange
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) },
        };
        var observer = new QueryObserver<string>(_client, Options(["todos"]));

        using var subscription = await SubscribeUntilFetchedAsync(observer);
        var staleBefore = observer.CurrentResult.IsStale;
        _time.Advance(TimeSpan.FromSeconds(31));

        Assert.False(staleBefore);
        Assert.True(observer.CurrentResult.IsStale);
    }

    [Fact]
    public async Task PerQueryStaleTimeZero_OverridesClientDefault()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) },
        };
        var observer = new QueryObserver<string>(_client, Options(["todos"]) with { StaleTime = TimeSpan.Zero });

        using var subscription = await SubscribeUntilFetchedAsync(observer);

        Assert.True(observer.CurrentResult.IsStale);
    }

    [Fact]
    public async Task KeyPrefixStaleTime_OverridesClientDefault()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromMinutes(1) },
        };
        _client.SetQueryDefaults(["todos"], new QueryDefaults { StaleTime = TimeSpan.FromSeconds(5) });
        var observer = new QueryObserver<string>(_client, Options(["todos", 1]));

        using var subscription = await SubscribeUntilFetchedAsync(observer);
        _time.Advance(TimeSpan.FromSeconds(6));

        Assert.True(observer.CurrentResult.IsStale);
    }

    [Fact]
    public async Task KeyPrefixDefaults_ApplyInRegistrationOrder()
    {
        _client.SetQueryDefaults(["todos"], new QueryDefaults
        {
            StaleTime = TimeSpan.FromMinutes(10),
            GcTime = TimeSpan.FromSeconds(20),
        });
        _client.SetQueryDefaults(["todos", 1], new QueryDefaults { StaleTime = TimeSpan.FromSeconds(5) });
        var observer = new QueryObserver<string>(_client, Options(["todos", 1]));

        var subscription = await SubscribeUntilFetchedAsync(observer);
        _time.Advance(TimeSpan.FromSeconds(6));
        var isStale = observer.CurrentResult.IsStale;
        subscription.Dispose();
        _time.Advance(TimeSpan.FromSeconds(21));

        Assert.True(isStale);
        Assert.False(IsCached(["todos", 1]));
    }

    [Fact]
    public async Task SetQueryDefaults_ReplacesEarlierRegistrationForSameKey()
    {
        _client.SetQueryDefaults(["todos"], new QueryDefaults { StaleTime = TimeSpan.FromMinutes(10) });
        _client.SetQueryDefaults(["todos"], new QueryDefaults { StaleTime = TimeSpan.FromSeconds(5) });
        var observer = new QueryObserver<string>(_client, Options(["todos"]));

        using var subscription = await SubscribeUntilFetchedAsync(observer);
        _time.Advance(TimeSpan.FromSeconds(6));

        Assert.True(observer.CurrentResult.IsStale);
    }

    [Fact]
    public async Task SetOptions_AppliesDefaultsForNewKey()
    {
        _client.SetQueryDefaults(["fresh"], new QueryDefaults { StaleTime = TimeSpan.FromMinutes(5) });
        var observer = new QueryObserver<string>(_client, Options(["stale"]));
        using var subscription = await SubscribeUntilFetchedAsync(observer);
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = observer.Subscribe(result =>
        {
            if (result is { IsSuccess: true, IsFetching: false, Data: "fresh" })
            {
                fetched.TrySetResult();
            }
        });

        observer.SetOptions(Options(["fresh"], data: "fresh"));
        await fetched.Task;

        Assert.False(observer.CurrentResult.IsStale);
    }

    [Fact]
    public async Task InfiniteQueryObserver_InheritsKeyPrefixStaleTime()
    {
        _client.SetQueryDefaults(["pages"], new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) });
        var observer = new InfiniteQueryObserver<string, int>(_client, new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["pages"],
            InitialPageParam = 0,
            GetNextPageParam = _ => PageParamResult<int>.None,
            QueryFn = _ => Task.FromResult("page"),
        });
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = observer.Subscribe(result =>
        {
            if (result is { IsSuccess: true, IsFetching: false })
            {
                fetched.TrySetResult();
            }
        });
        await fetched.Task;

        Assert.False(observer.CurrentResult.IsStale);
    }


    [Fact]
    public async Task KeyPrefixGcTime_AppliesToObserverCreatedQuery()
    {
        _client.SetQueryDefaults(["todos"], new QueryDefaults { GcTime = TimeSpan.FromSeconds(10) });
        var observer = new QueryObserver<string>(_client, Options(["todos", 1]));
        var subscription = await SubscribeUntilFetchedAsync(observer);

        subscription.Dispose();
        _time.Advance(TimeSpan.FromSeconds(9));
        var cachedBeforeGcTime = IsCached(["todos", 1]);
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(cachedBeforeGcTime);
        Assert.False(IsCached(["todos", 1]));
    }

    [Fact]
    public async Task ClientDefaultGcTime_AppliesToObserverCreatedQuery()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { GcTime = TimeSpan.FromSeconds(10) },
        };
        var observer = new QueryObserver<string>(_client, Options(["todos"]));
        var subscription = await SubscribeUntilFetchedAsync(observer);

        subscription.Dispose();
        _time.Advance(TimeSpan.FromSeconds(11));

        Assert.False(IsCached(["todos"]));
    }

    [Fact]
    public async Task PerQueryGcTimeZero_RemovesQueryAsSoonAsUnused()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { GcTime = TimeSpan.FromMinutes(10) },
        };
        var observer = new QueryObserver<string>(_client, Options(["todos"]) with { GcTime = TimeSpan.Zero });
        var subscription = await SubscribeUntilFetchedAsync(observer);

        subscription.Dispose();
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.False(IsCached(["todos"]));
    }

    [Fact]
    public void SetQueryData_UsesKeyPrefixGcTime()
    {
        _client.SetQueryDefaults(["todos"], new QueryDefaults { GcTime = TimeSpan.FromSeconds(10) });

        _client.SetQueryData(["todos"], "cached");
        _time.Advance(TimeSpan.FromSeconds(11));

        Assert.False(IsCached(["todos"]));
    }


    [Theory]
    [InlineData(DefaultsLayer.KeyPrefix)]
    [InlineData(DefaultsLayer.Client)]
    public async Task RefetchOnWindowFocusNever_FromDefaults_SuppressesFocusRefetch(DefaultsLayer layer)
    {
        SetDefaults(layer, new QueryDefaults { RefetchOnWindowFocus = RefetchOnBehavior.Never });
        var fetchCount = 0;
        var observer = new QueryObserver<string>(_client, Options(["todos"], () => fetchCount++));
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        _focusManager.SetFocused(false);
        _focusManager.SetFocused(true);

        Assert.Equal(0, _client.IsFetching());
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task PerQueryRefetchOnWindowFocusWhenStale_OverridesKeyPrefixNever()
    {
        _client.SetQueryDefaults(["todos"], new QueryDefaults { RefetchOnWindowFocus = RefetchOnBehavior.Never });
        var refetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;
        var options = Options(["todos"], () =>
        {
            if (++fetchCount == 2)
            {
                refetched.TrySetResult();
            }
        }) with { RefetchOnWindowFocus = RefetchOnBehavior.WhenStale };
        var observer = new QueryObserver<string>(_client, options);
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        _focusManager.SetFocused(false);
        _focusManager.SetFocused(true);
        await refetched.Task;

        Assert.Equal(2, fetchCount);
    }

    [Theory]
    [InlineData(DefaultsLayer.KeyPrefix)]
    [InlineData(DefaultsLayer.Client)]
    public async Task RefetchOnReconnectNever_FromDefaults_SuppressesReconnectRefetch(DefaultsLayer layer)
    {
        SetDefaults(layer, new QueryDefaults { RefetchOnReconnect = RefetchOnBehavior.Never });
        var fetchCount = 0;
        var observer = new QueryObserver<string>(_client, Options(["todos"], () => fetchCount++));
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        _onlineManager.SetOnline(false);
        _onlineManager.SetOnline(true);

        Assert.Equal(0, _client.IsFetching());
        Assert.Equal(1, fetchCount);
    }

    [Theory]
    [InlineData(DefaultsLayer.KeyPrefix)]
    [InlineData(DefaultsLayer.Client)]
    public async Task RefetchOnMountNever_FromDefaults_SuppressesMountRefetch(DefaultsLayer layer)
    {
        SetDefaults(layer, new QueryDefaults { RefetchOnMount = RefetchOnBehavior.Never });
        var fetchCount = 0;
        var first = new QueryObserver<string>(_client, Options(["todos"], () => fetchCount++));
        using var firstSubscription = await SubscribeUntilFetchedAsync(first);
        var second = new QueryObserver<string>(_client, Options(["todos"], () => fetchCount++));

        using var secondSubscription = second.Subscribe(_ => { });

        Assert.Equal(0, _client.IsFetching());
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task NetworkModeAlways_WithoutRefetchOnReconnect_DoesNotRefetchOnReconnect()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { NetworkMode = NetworkMode.Always },
        };
        var fetchCount = 0;
        var observer = new QueryObserver<string>(_client, Options(["todos"], () => fetchCount++));
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        _onlineManager.SetOnline(false);
        _onlineManager.SetOnline(true);

        Assert.Equal(0, _client.IsFetching());
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task NetworkModeOnline_WithoutRefetchOnReconnect_RefetchesStaleDataOnReconnect()
    {
        var refetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;
        var observer = new QueryObserver<string>(_client, Options(["todos"], () =>
        {
            if (++fetchCount == 2)
            {
                refetched.TrySetResult();
            }
        }));
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        _onlineManager.SetOnline(false);
        _onlineManager.SetOnline(true);
        await refetched.Task;

        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task ClientDefaultNetworkModeAlways_FetchesWhileOffline()
    {
        // Arrange
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { NetworkMode = NetworkMode.Always },
        };
        _onlineManager.SetOnline(false);
        var observer = new QueryObserver<string>(_client, Options(["todos"]));

        // Act
        using var subscription = await SubscribeUntilFetchedAsync(observer);

        Assert.Equal("data", observer.CurrentResult.Data);
    }


    [Fact]
    public async Task FetchQueryAsync_UsesClientDefaultRetry()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { Retry = 1, RetryDelay = (_, _) => TimeSpan.Zero },
        };
        var attempts = 0;

        // Act
        var data = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = _ => ++attempts == 1
                ? throw new InvalidOperationException("first attempt fails")
                : Task.FromResult("todos"),
        });

        // Assert
        Assert.Equal("todos", data);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task FetchQueryAsync_DoesNotRetry_WhenNoLayerSetsRetry()
    {
        var attempts = 0;

        Task Act() => _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = _ =>
            {
                attempts++;
                throw new InvalidOperationException("fails");
            },
        });

        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task FetchQueryAsync_UsesClientDefaultStaleTime()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) },
        };
        _client.SetQueryData(["todos"], "cached");
        var fetchCount = 0;

        // Act
        var data = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = _ =>
            {
                fetchCount++;
                return Task.FromResult("fetched");
            },
        });

        Assert.Equal("cached", data);
        Assert.Equal(0, fetchCount);
    }

    [Fact]
    public async Task FetchQueryAsync_PerQueryStaleTimeZero_OverridesClientDefault()
    {
        // Arrange
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) },
        };
        _client.SetQueryData(["todos"], "cached");

        // Act
        var data = await _client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.Zero,
        });

        // Assert
        Assert.Equal("fetched", data);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_UsesKeyPrefixStaleTime()
    {
        // Arrange
        _client.SetQueryDefaults(["todos"], new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) });
        _client.SetQueryData(["todos"], "cached");
        _time.Advance(TimeSpan.FromSeconds(10));

        var data = await _client.EnsureQueryDataAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = _ => Task.FromResult("fetched"),
        });

        Assert.Equal("cached", data);
    }

    [Fact]
    public async Task FetchInfiniteQueryAsync_UsesKeyPrefixRetry()
    {
        _client.SetQueryDefaults(["pages"], new QueryDefaults { Retry = 1, RetryDelay = (_, _) => TimeSpan.Zero });
        var attempts = 0;

        // Act
        var data = await _client.FetchInfiniteQueryAsync(new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["pages"],
            InitialPageParam = 0,
            GetNextPageParam = _ => PageParamResult<int>.None,
            QueryFn = _ => ++attempts == 1
                ? throw new InvalidOperationException("first attempt fails")
                : Task.FromResult("page"),
        });

        Assert.Equal(["page"], data.Pages);
        Assert.Equal(2, attempts);
    }


    [Fact]
    public void DefaultQueryOptions_ResolvesEveryLayer_AndIsIdempotent()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { Retry = 5, StaleTime = TimeSpan.FromSeconds(30) },
        };
        _client.SetQueryDefaults(["todos"], new QueryDefaults { RefetchOnMount = RefetchOnBehavior.Never });
        var options = Options(["todos", 1]) with { GcTime = TimeSpan.Zero };

        var defaulted = _client.DefaultQueryOptions(options);
        var again = _client.DefaultQueryOptions(defaulted);

        Assert.Equal(5, defaulted.Retry);
        Assert.Equal(TimeSpan.FromSeconds(30), defaulted.StaleTime);
        Assert.Equal(RefetchOnBehavior.Never, defaulted.RefetchOnMount);
        Assert.Equal(TimeSpan.Zero, defaulted.GcTime);
        Assert.Equal(RefetchOnBehavior.WhenStale, defaulted.RefetchOnWindowFocus);
        Assert.Equal(NetworkMode.Online, defaulted.NetworkMode);
        Assert.Equal(defaulted, again);
    }

    [Fact]
    public void DefaultQueryOptions_SkipToken_DisablesQuery()
    {
        var options = new QueryObserverOptions<string> { QueryKey = ["todos"], QueryFn = SkipToken.QueryFn<string>() };

        var defaulted = _client.DefaultQueryOptions(options);

        // Assert
        Assert.False(defaulted.Enabled);
    }

    [Fact]
    public void GetQueryDefaults_MergesMatchingRegistrations_WithoutClientDefaults()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { StaleTime = TimeSpan.FromSeconds(30) },
        };
        _client.SetQueryDefaults(["todos"], new QueryDefaults { Retry = 1 });
        _client.SetQueryDefaults(["users"], new QueryDefaults { Retry = 2 });

        // Act
        var defaults = _client.GetQueryDefaults(["todos", 1]);

        // Assert
        Assert.Equal(new QueryDefaults { Retry = 1 }, defaults);
    }

    [Fact]
    public void QueryConfiguration_DefaultsFromEveryLayer()
    {
        _client.DefaultOptions = new QueryClientDefaultOptions
        {
            Queries = new QueryDefaults { Retry = 5, NetworkMode = NetworkMode.OfflineFirst },
        };
        _client.SetQueryDefaults(["todos"], new QueryDefaults { GcTime = TimeSpan.FromSeconds(10) });

        var query = _client.QueryCache.GetOrCreate<string, string>(
            _client, new QueryConfiguration<string> { QueryKey = ["todos", 1], Retry = 0 });

        Assert.Equal(TimeSpan.FromSeconds(10), query.Options.GcTime);
        Assert.Equal(0, query.Options.Retry);
        Assert.Equal(NetworkMode.OfflineFirst, query.Options.NetworkMode);
    }


    public enum DefaultsLayer
    {
        KeyPrefix,
        Client,
    }

    private void SetDefaults(DefaultsLayer layer, QueryDefaults defaults)
    {
        if (layer is DefaultsLayer.KeyPrefix)
        {
            _client.SetQueryDefaults(["todos"], defaults);
        }
        else
        {
            _client.DefaultOptions = new QueryClientDefaultOptions { Queries = defaults };
        }
    }

    private bool IsCached(QueryKey queryKey)
        => _client.QueryCache.Find(new QueryFilters { QueryKey = queryKey, Exact = true }) is not null;

    private static QueryObserverOptions<string> Options(
        QueryKey queryKey, Action? onFetch = null, string data = "data") => new()
    {
        QueryKey = queryKey,
        QueryFn = _ =>
        {
            onFetch?.Invoke();
            return Task.FromResult(data);
        },
    };

    private static async Task<IDisposable> SubscribeUntilFetchedAsync(QueryObserver<string> observer)
    {
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = observer.Subscribe(result =>
        {
            if (result is { IsSuccess: true, IsFetching: false })
            {
                fetched.TrySetResult();
            }
        });

        if (observer.CurrentResult is { IsSuccess: true, IsFetching: false })
        {
            fetched.TrySetResult();
        }

        await fetched.Task;
        return subscription;
    }
}
