#pragma warning disable CS0618 // Covers, or sets up state with, the obsolete imperative fetch methods.

using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

// TanStack references: query.setOptions/fetch, queryObserver.setOptions/#executeFetch,
// removable.updateGcTime (@tanstack/query-core 5.105.3).
public sealed class QueryOptionsUpdateTests
{
    private static QueryClient CreateQueryClient(
        IFocusManager? focusManager = null,
        IOnlineManager? onlineManager = null,
        TimeProvider? timeProvider = null)
    {
        return new QueryClient(
            new QueryCache(),
            timeProvider: timeProvider,
            focusManager: focusManager,
            onlineManager: onlineManager)
        {
            DefaultOptions = new QueryClientDefaultOptions
            {
                Queries = new QueryDefaults { RetryDelay = (_, _) => TimeSpan.Zero },
            },
        };
    }

    private sealed class CountingQueryFn
    {
        public int Attempts;

        public bool Failing { get; set; } = true;

        public Task<string> Invoke(QueryFunctionContext _)
        {
            if (!Failing)
            {
                return Task.FromResult("data");
            }

            Interlocked.Increment(ref Attempts);
            throw new InvalidOperationException("boom");
        }
    }

    private static async Task<IDisposable> MountUntilErrored<TData>(QueryObserver<TData, TData> observer)
    {
        var errored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = observer.Subscribe(result =>
        {
            if (result is { IsError: true, FetchStatus: FetchStatus.Idle })
            {
                errored.TrySetResult();
            }
        });

        await errored.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return subscription;
    }

    private static async Task<IDisposable> MountUntilSucceeded<TData>(QueryObserver<TData, TData> observer)
    {
        var succeeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = observer.Subscribe(result =>
        {
            if (result is { IsSuccess: true, FetchStatus: FetchStatus.Idle })
            {
                succeeded.TrySetResult();
            }
        });

        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return subscription;
    }

    private static QueryObserver<string, string> CreateObserver(
        QueryClient client,
        CountingQueryFn fn,
        int? retry = null,
        bool enabled = true,
        TimeSpan? staleTime = null,
        RefetchOnBehavior? refetchOnWindowFocus = null,
        RefetchOnBehavior? refetchOnReconnect = null)
    {
        return new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["shared"],
            QueryFn = fn.Invoke,
            Retry = retry,
            Enabled = enabled,
            StaleTime = staleTime,
            RefetchOnWindowFocus = refetchOnWindowFocus,
            RefetchOnReconnect = refetchOnReconnect,
        });
    }

    [Fact(Timeout = 10_000)]
    public async Task Observer_Retry_Applies_To_New_Query()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn();
        var observer = CreateObserver(client, fn, retry: 0);

        using var subscription = await MountUntilErrored(observer);

        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Observer_Retry_Applies_To_Query_Created_By_SetQueryData()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn();
        client.SetQueryData<string>(["shared"], "seed");
        var observer = CreateObserver(client, fn, retry: 0);

        using var subscription = await MountUntilErrored(observer);

        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Later_Observer_Retry_Replaces_Earlier_Observer_Retry()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn();
        _ = CreateObserver(client, fn, enabled: false);
        var observer = CreateObserver(client, fn, retry: 0);

        using var subscription = await MountUntilErrored(observer);

        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Observer_With_Unset_Retry_Uses_Default_After_Failed_Prefetch()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn();
        await client.PrefetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["shared"],
            QueryFn = _ => throw new InvalidOperationException("prefetch"),
        });
        var observer = CreateObserver(client, fn);

        using var subscription = await MountUntilErrored(observer);

        Assert.Equal(4, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task SetOptions_With_Same_Key_Replaces_Query_Retry()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn { Failing = false };
        var observer = CreateObserver(client, fn, retry: 3);
        using var subscription = await MountUntilSucceeded(observer);

        observer.SetOptions(observer.Options with { Retry = 0, StaleTime = TimeSpan.FromHours(1) });
        var retryAfterSetOptions = observer.CurrentQuery?.Options.Retry;
        fn.Failing = true;
        await observer.RefetchAsync();

        Assert.Equal(0, retryAfterSetOptions);
        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task FetchQuery_Retry_Replaces_Retry_Of_Existing_Query()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn();
        _ = CreateObserver(client, fn, retry: 3, enabled: false);

        Task Act() => client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["shared"],
            QueryFn = fn.Invoke,
            Retry = 0,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Equal(1, fn.Attempts);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    public async Task Refetch_Uses_Refetching_Observers_Retry_With_Sibling_Mounted(bool refetchA, int expectedAttempts)
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn { Failing = false };
        var a = CreateObserver(client, fn, retry: 0, staleTime: TimeSpan.FromHours(1));
        var b = CreateObserver(client, fn, retry: 2, staleTime: TimeSpan.FromHours(1));
        using var subscriptionA = await MountUntilSucceeded(a);
        using var subscriptionB = b.Subscribe(_ => { });
        fn.Failing = true;

        await (refetchA ? a : b).RefetchAsync();

        Assert.Equal(expectedAttempts, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task FetchQuery_Options_Govern_Later_Client_Refetches()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn { Failing = false };
        var observer = CreateObserver(client, fn, retry: 2);
        using var subscription = await MountUntilSucceeded(observer);
        fn.Failing = true;

        try
        {
            await client.FetchQueryAsync(new FetchQueryOptions<string> { QueryKey = ["shared"], QueryFn = fn.Invoke });
        }
        catch (InvalidOperationException)
        {
        }

        var fetchQueryAttempts = fn.Attempts;
        await client.InvalidateQueriesAsync(["shared"]);

        Assert.Equal(1, fetchQueryAttempts);
        Assert.Equal(2, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Joining_InFlight_Fetch_Leaves_Options_Unchanged()
    {
        var client = CreateQueryClient();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["shared"],
            QueryFn = _ => release.Task,
            Retry = 1,
        });
        using var subscription = observer.Subscribe(_ => { });

        var joined = client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["shared"],
            QueryFn = _ => release.Task,
            Retry = 5,
        });
        var retryWhileInFlight = observer.CurrentQuery?.Options.Retry;
        release.SetResult("data");
        await joined;

        Assert.Equal(1, retryWhileInFlight);
    }

    [Fact(Timeout = 10_000)]
    public async Task Focus_Refetch_Uses_Options_Of_Observer_That_Wants_It()
    {
        var focusManager = new FocusManager();
        var client = CreateQueryClient(focusManager: focusManager);
        var fn = new CountingQueryFn { Failing = false };
        var ignoresFocus = CreateObserver(client, fn, retry: 2, refetchOnWindowFocus: RefetchOnBehavior.Never);
        using var ignoringSubscription = await MountUntilSucceeded(ignoresFocus);
        var wantsFocus = CreateObserver(client, fn, retry: 0, refetchOnWindowFocus: RefetchOnBehavior.Always);
        using var subscription = await MountUntilSucceeded(wantsFocus);

        await ignoresFocus.RefetchAsync();
        focusManager.SetFocused(false);
        fn.Failing = true;

        focusManager.SetFocused(true);
        await WaitForIdle(wantsFocus);

        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Reconnect_Refetch_Uses_Options_Of_Observer_That_Wants_It()
    {
        var onlineManager = new OnlineManager();
        var client = CreateQueryClient(onlineManager: onlineManager);
        var fn = new CountingQueryFn { Failing = false };
        var ignoresReconnect = CreateObserver(client, fn, retry: 2, refetchOnReconnect: RefetchOnBehavior.Never);
        using var ignoringSubscription = await MountUntilSucceeded(ignoresReconnect);
        var wantsReconnect = CreateObserver(client, fn, retry: 0, refetchOnReconnect: RefetchOnBehavior.Always);
        using var subscription = await MountUntilSucceeded(wantsReconnect);

        await ignoresReconnect.RefetchAsync();
        onlineManager.SetOnline(false);
        fn.Failing = true;

        onlineManager.SetOnline(true);
        await WaitForIdle(wantsReconnect);

        Assert.Equal(1, fn.Attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task FetchNextPage_Uses_Infinite_Observer_Options()
    {
        var client = CreateQueryClient();
        var failing = false;
        var attempts = 0;
        Task<string> QueryFn(InfiniteQueryFunctionContext<int> ctx)
        {
            if (!failing) return Task.FromResult($"page-{ctx.PageParam}");
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("boom");
        }

        var fetchOptions = new FetchInfiniteQueryOptions<string, int>
        {
            QueryKey = ["infinite"],
            QueryFn = QueryFn,
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
            Retry = 0,
        };

        await client.FetchInfiniteQueryAsync(fetchOptions);
        var observer = new InfiniteQueryObserver<string, int>(client, new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["infinite"],
            QueryFn = QueryFn,
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
        });
        await client.FetchInfiniteQueryAsync(fetchOptions);
        failing = true;

        await observer.FetchNextPageAsync();

        Assert.Equal(4, attempts);
    }

    [Fact(Timeout = 10_000)]
    public async Task Refetch_After_Query_Removal_Fetches_Into_Cache()
    {
        var client = CreateQueryClient();
        var fn = new CountingQueryFn { Failing = false };
        var observer = CreateObserver(client, fn);
        using var subscription = await MountUntilSucceeded(observer);
        client.RemoveQueries();

        await observer.RefetchAsync();

        Assert.Equal("data", client.GetQueryData<string>(["shared"]));
    }

    [Fact(Timeout = 10_000)]
    public void Concurrent_Observers_Keep_Largest_GcTime()
    {
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider: timeProvider);
        const int observerCount = 64;
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["shared"]);

        Parallel.For(1, observerCount + 1, i =>
            _ = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
            {
                QueryKey = ["shared"],
                QueryFn = _ => Task.FromResult("data"),
                GcTime = TimeSpan.FromMinutes(i),
                Enabled = false,
            }));
        var last = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["shared"],
            QueryFn = _ => Task.FromResult("data"),
            GcTime = TimeSpan.FromMinutes(1),
            Enabled = false,
        });
        last.Subscribe(_ => { }).Dispose();
        timeProvider.Advance(TimeSpan.FromMinutes(observerCount) - TimeSpan.FromSeconds(1));
        var presentBeforeLargestGcTime = client.QueryCache.GetByHash(hash) is not null;
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.True(presentBeforeLargestGcTime);
        Assert.Null(client.QueryCache.GetByHash(hash));
    }

    private static async Task WaitForIdle(QueryObserver<string, string> observer)
    {
        if (observer.CurrentQuery is { CurrentFetchStatus: not FetchStatus.Idle } query)
        {
            try
            {
                await query.Fetch();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
