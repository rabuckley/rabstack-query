using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

public sealed class QueryObserverSelectErrorTests
{
    private static readonly RefetchOptions JoinInFlightFetch = new() { CancelRefetch = false };

    private static QueryClient CreateClient(TimeProvider? timeProvider = null)
        => timeProvider is null
            ? new QueryClient(new QueryCache())
            : new QueryClient(new QueryCache(), timeProvider: timeProvider);

    // Join before releasing the gate, or a completed fetch can trigger another that never finishes.
    private static async Task CompleteFetchAsync<TData, TQueryData>(
        QueryObserver<TData, TQueryData> observer, SemaphoreSlim gate)
    {
        var fetch = observer.RefetchAsync(JoinInFlightFetch);
        gate.Release();
        await fetch;
    }

    // queryObserver.test.tsx, throwing-selector refetch states.
    [Fact(Timeout = 10_000)]
    public async Task Select_Throwing_On_Each_Fetch_Reports_Error_Without_Data()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var results = new List<IQueryResult<string>>();
        var observer = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = ["select-always-throws"],
            QueryFn = async _ => { await gate.WaitAsync(); return new Box(1); },
            Select = _ => throw new InvalidOperationException("selector error"),
        });

        using var subscription = observer.Subscribe(results.Add);
        await CompleteFetchAsync(observer, gate);
        var refetch = observer.RefetchAsync();
        gate.Release();
        await refetch;

        Assert.Collection(
            results,
            r => AssertResult(r, QueryStatus.Pending, FetchStatus.Fetching, null),
            r => AssertResult(r, QueryStatus.Errored, FetchStatus.Idle, null),
            r => AssertResult(r, QueryStatus.Errored, FetchStatus.Fetching, null),
            r => AssertResult(r, QueryStatus.Errored, FetchStatus.Idle, null));
    }

    // queryObserver.test.tsx: "should not have isPlaceholderData true when selector throws on
    // placeholder data"
    [Fact]
    public void Select_Throwing_On_Placeholder_Data_Is_Not_Placeholder()
    {
        var client = CreateClient();
        var placeholder = new Box(0);

        var observer = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = ["select-placeholder"],
            QueryFn = _ => Task.FromResult(new Box(1)),
            PlaceholderData = (_, _) => placeholder,
            Select = _ => throw new InvalidOperationException("selector error"),
        });

        var result = observer.CurrentResult;
        Assert.True(result.IsError);
        Assert.False(result.IsPlaceholderData);
        Assert.Null(result.Data);
    }

    // queryObserver.test.tsx: "should return stale data if selector throws an error"
    [Fact(Timeout = 10_000)]
    public async Task Select_Throwing_After_Success_Keeps_Last_Selected_Data()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var error = new InvalidOperationException("select error");
        var shouldThrow = false;
        var fetches = 0;
        var results = new List<IQueryResult<string>>();
        var observer = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = ["select-stale-data"],
            QueryFn = async _ => { await gate.WaitAsync(); return new Box(++fetches); },
            Select = box =>
            {
                if (shouldThrow) throw error;
                shouldThrow = true;
                return box.Value.ToString();
            },
        });

        using var subscription = observer.Subscribe(results.Add);
        await CompleteFetchAsync(observer, gate);
        var refetch = observer.RefetchAsync();
        gate.Release();
        await refetch;

        Assert.Collection(
            results,
            r => AssertResult(r, QueryStatus.Pending, FetchStatus.Fetching, null),
            r => AssertResult(r, QueryStatus.Succeeded, FetchStatus.Idle, "1"),
            r => AssertResult(r, QueryStatus.Succeeded, FetchStatus.Fetching, "1"),
            r =>
            {
                AssertResult(r, QueryStatus.Errored, FetchStatus.Idle, "1");
                Assert.Same(error, r.Error);
            });
    }

    // queryObserver.test.tsx: "should not leak the select error of the previous query into the
    // result of a different query"
    [Fact(Timeout = 10_000)]
    public async Task Select_Error_Does_Not_Leak_Into_A_Different_Query()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        Func<QueryFunctionContext, Task<Box>> queryFn = async _ => { await gate.WaitAsync(); return new Box(1); };
        var observer = new QueryObserver<Box, Box>(client, new QueryObserverOptions<Box, Box>
        {
            QueryKey = ["select-leak-1"],
            QueryFn = queryFn,
            Select = _ => throw new InvalidOperationException("selector error"),
        });
        using var subscription = observer.Subscribe(_ => { });
        await CompleteFetchAsync(observer, gate);
        Assert.True(observer.CurrentResult.IsError);

        observer.SetOptions(new QueryObserverOptions<Box, Box>
        {
            QueryKey = ["select-leak-2"],
            QueryFn = queryFn,
            Select = box => box,
        });

        AssertResult(observer.CurrentResult, QueryStatus.Pending, FetchStatus.Fetching, null);
        await CompleteFetchAsync(observer, gate);
        Assert.Equal(QueryStatus.Succeeded, observer.CurrentResult.Status);
        Assert.Equal(1, observer.CurrentResult.Data?.Value);
        Assert.Null(observer.CurrentResult.Error);
    }

    // queryObserver.test.tsx: "should not leak a stale select error through the memoized
    // placeholderData path"
    [Fact(Timeout = 10_000)]
    public async Task Select_Error_Does_Not_Leak_Through_Placeholder_Data()
    {
        var client = CreateClient();
        Func<Box?, Query<Box>?, Box?> placeholderData = (_, _) => new Box(0);
        QueryObserverOptions<string, Box> Options(string key, SemaphoreSlim gate, Func<Box, string> select) => new()
        {
            QueryKey = [key],
            QueryFn = async _ => { await gate.WaitAsync(); return new Box(1); },
            PlaceholderData = placeholderData,
            Select = select,
        };
        var gateA = new SemaphoreSlim(0);
        var gateC = new SemaphoreSlim(0);
        var observer = new QueryObserver<string, Box>(
            client, Options("select-placeholder-a", gateA, box => box.Value.ToString()));
        using var subscription = observer.Subscribe(_ => { });
        await CompleteFetchAsync(observer, gateA);
        Assert.Equal("1", observer.CurrentResult.Data);

        observer.SetOptions(Options(
            "select-placeholder-b",
            new SemaphoreSlim(0),
            _ => throw new InvalidOperationException("selector error")));
        Assert.True(observer.CurrentResult.IsError);

        observer.SetOptions(Options("select-placeholder-c", gateC, box => box.Value.ToString()));
        var placeholderResult = observer.CurrentResult;
        await CompleteFetchAsync(observer, gateC);

        Assert.Equal(QueryStatus.Succeeded, placeholderResult.Status);
        Assert.Equal("0", placeholderResult.Data);
        Assert.Null(placeholderResult.Error);
        Assert.True(placeholderResult.IsPlaceholderData);
        Assert.Equal("1", observer.CurrentResult.Data);
        Assert.Null(observer.CurrentResult.Error);
    }

    // queryObserver.test.tsx: "should clear the select error when the query is reset"
    [Fact(Timeout = 10_000)]
    public async Task Select_Error_Clears_When_Query_Is_Reset()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var shouldThrow = true;
        var observer = new QueryObserver<Box, Box>(client, new QueryObserverOptions<Box, Box>
        {
            QueryKey = ["select-reset"],
            QueryFn = async _ => { await gate.WaitAsync(); return new Box(1); },
            Select = box => shouldThrow ? throw new InvalidOperationException("selector error") : box,
        });
        using var subscription = observer.Subscribe(_ => { });
        await CompleteFetchAsync(observer, gate);
        Assert.True(observer.CurrentResult.IsError);

        shouldThrow = false;
        client.ResetQueries(new QueryFilters { QueryKey = ["select-reset"] });

        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);
        Assert.Null(observer.CurrentResult.Data);
        Assert.Null(observer.CurrentResult.Error);
        await CompleteFetchAsync(observer, gate);
        Assert.Equal(QueryStatus.Succeeded, observer.CurrentResult.Status);
        Assert.Null(observer.CurrentResult.Error);
    }

    // useQuery.test.tsx, stable throwing selector on rerender. SetOptions models the rerender.
    [Fact(Timeout = 10_000)]
    public async Task Stable_Throwing_Select_Runs_Once_Per_Data_Change()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var runs = 0;
        Func<Box, string> select = _ =>
        {
            runs++;
            throw new InvalidOperationException("selector error");
        };
        var options = new QueryObserverOptions<string, Box>
        {
            QueryKey = ["select-stable"],
            QueryFn = async _ => { await gate.WaitAsync(); return new Box(1); },
            Select = select,
        };
        var observer = new QueryObserver<string, Box>(client, options);
        using var subscription = observer.Subscribe(_ => { });
        await CompleteFetchAsync(observer, gate);
        Assert.Equal(1, runs);

        observer.SetOptions(options with { });
        Assert.Equal(1, runs);

        var refetch = observer.RefetchAsync();
        gate.Release();
        await refetch;
        Assert.Equal(2, runs);
    }

    [Fact(Timeout = 10_000)]
    public async Task Stable_Select_Over_Value_Type_Does_Not_Rerun_When_Only_Fetch_Status_Changes()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var runs = 0;
        var observer = new QueryObserver<string, int>(client, new QueryObserverOptions<string, int>
        {
            QueryKey = ["select-value-type-memo"],
            QueryFn = async _ => { await gate.WaitAsync(); return 7; },
            Select = value => { runs++; return value.ToString(); },
        });
        using var subscription = observer.Subscribe(_ => { });
        await CompleteFetchAsync(observer, gate);

        var refetch = observer.RefetchAsync();
        var runsWhileFetching = runs;
        gate.Release();
        await refetch;

        Assert.Equal(1, runsWhileFetching);
        Assert.Equal(1, runs);
    }

    // TanStack leaves missing data undefined; a pending C# int query instead holds default(int).
    [Fact]
    public void Select_Does_Not_Run_On_Value_Type_Query_Without_Data()
    {
        var client = CreateClient();
        var runs = 0;

        var observer = new QueryObserver<string, int>(client, new QueryObserverOptions<string, int>
        {
            QueryKey = ["select-value-type-pending"],
            QueryFn = _ => Task.FromResult(1),
            Select = value => { runs++; return value.ToString(); },
        });

        Assert.Equal(0, runs);
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);
    }

    // A fetched 0 equals the default(int) held while pending, so it must still count as new data.
    [Fact(Timeout = 10_000)]
    public async Task Select_Runs_When_Value_Type_Query_First_Fetches_Default_Value()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        var observer = new QueryObserver<string, int>(client, new QueryObserverOptions<string, int>
        {
            QueryKey = ["select-value-type-default"],
            QueryFn = async _ => { await gate.WaitAsync(); return 0; },
            Select = value => $"selected {value}",
        });
        using var subscription = observer.Subscribe(_ => { });

        await CompleteFetchAsync(observer, gate);

        Assert.Equal("selected 0", observer.CurrentResult.Data);
    }

    // queryObserver.ts, createResult catches selector errors before they reach query state.
    [Fact(Timeout = 10_000)]
    public async Task Throwing_Select_Does_Not_Affect_Query_Or_Observers_On_Same_Key()
    {
        var client = CreateClient();
        var gate = new SemaphoreSlim(0);
        QueryKey key = ["select-isolation"];
        Func<QueryFunctionContext, Task<Box>> queryFn = async _ => { await gate.WaitAsync(); return new Box(1); };
        var failing = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = key,
            QueryFn = queryFn,
            Select = _ => throw new InvalidOperationException("selector error"),
        });
        var healthy = new QueryObserver<Box, Box>(client, new QueryObserverOptions<Box, Box>
        {
            QueryKey = key,
            QueryFn = queryFn,
        });
        var healthyStatuses = new List<QueryStatus>();
        using var failingSubscription = failing.Subscribe(_ => { });
        using var healthySubscription = healthy.Subscribe(r => healthyStatuses.Add(r.Status));

        await CompleteFetchAsync(healthy, gate);

        Assert.True(failing.CurrentResult.IsError);
        Assert.Equal(QueryStatus.Succeeded, healthy.CurrentResult.Status);
        Assert.Null(healthy.CurrentResult.Error);
        Assert.DoesNotContain(QueryStatus.Errored, healthyStatuses);
        Assert.Equal(QueryStatus.Succeeded, failing.CurrentQuery?.State?.Status);
        Assert.Equal(1, client.GetQueryData<Box>(key)?.Value);
    }

    [Fact(Timeout = 10_000)]
    public async Task FetchQueryAsync_Returns_Data_When_Mounted_Select_Throws()
    {
        var client = CreateClient();
        QueryKey key = ["select-fetch-query"];
        var failing = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = _ => throw new InvalidOperationException("selector error"),
        });
        using var subscription = failing.Subscribe(_ => { });

        var data = await client.FetchQueryAsync(new FetchQueryOptions<Box>
        {
            QueryKey = key,
            QueryFn = _ => Task.FromResult(new Box(1)),
        });

        Assert.Equal(1, data.Value);
        Assert.True(failing.CurrentResult.IsError);
    }

    [Fact]
    public void SetQueryData_Returns_Normally_When_Mounted_Select_Throws()
    {
        var client = CreateClient();
        QueryKey key = ["select-set-data"];
        var failing = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = _ => throw new InvalidOperationException("selector error"),
        });
        using var subscription = failing.Subscribe(_ => { });

        client.SetQueryData(key, new Box(1));

        Assert.Equal(1, client.GetQueryData<Box>(key)?.Value);
        Assert.True(failing.CurrentResult.IsError);
    }

    [Fact]
    public void Constructing_Over_Cached_Data_Reports_Select_Error_As_Loading_Error()
    {
        var client = CreateClient();
        QueryKey key = ["select-ctor"];
        client.SetQueryData(key, new Box(1));

        var observer = new QueryObserver<int, Box>(client, new QueryObserverOptions<int, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = _ => throw new InvalidOperationException("selector error"),
        });

        // queryObserver.ts, createResult derives error flags from result data, not query update counts.
        var result = observer.CurrentResult;
        Assert.True(result.IsError);
        Assert.True(result.IsLoadingError);
        Assert.False(result.IsRefetchError);
    }

    [Fact]
    public void Select_Error_After_Selected_Value_Type_Is_A_Refetch_Error()
    {
        var client = CreateClient();
        QueryKey key = ["select-refetch-error"];
        client.SetQueryData(key, new Box(1));
        var shouldThrow = false;
        var observer = new QueryObserver<int, Box>(client, new QueryObserverOptions<int, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = box => shouldThrow ? throw new InvalidOperationException("selector error") : box.Value,
        });
        using var subscription = observer.Subscribe(_ => { });

        shouldThrow = true;
        client.SetQueryData(key, new Box(2));

        var result = observer.CurrentResult;
        Assert.Equal(1, result.Data);
        Assert.True(result.IsRefetchError);
        Assert.False(result.IsLoadingError);
    }

    [Fact]
    public void Throwing_StructuralSharing_On_Select_Output_Is_A_Select_Error()
    {
        var client = CreateClient();
        QueryKey key = ["select-structural-sharing"];
        client.SetQueryData(key, new Box(1));
        var error = new InvalidOperationException("sharing error");
        var observer = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = box => box.Value.ToString(),
            StructuralSharing = (_, _) => throw error,
        });
        using var subscription = observer.Subscribe(_ => { });

        client.SetQueryData(key, new Box(2));

        Assert.Same(error, observer.CurrentResult.Error);
        Assert.Equal("1", observer.CurrentResult.Data);
    }

    [Fact]
    public void Select_Error_Takes_ErrorUpdatedAt_From_TimeProvider()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(5_000));
        var client = CreateClient(timeProvider);
        QueryKey key = ["select-error-updated-at"];
        client.SetQueryData(key, new Box(1));

        var observer = new QueryObserver<string, Box>(client, new QueryObserverOptions<string, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = _ => throw new InvalidOperationException("selector error"),
        });

        Assert.Equal(timeProvider.GetUtcNow(), observer.CurrentResult.ErrorUpdatedAt);
    }

    // queryObserver.ts, createResult keeps the select error when only the selector is removed.
    [Fact]
    public void Removing_Select_While_Data_Exists_Keeps_The_Error()
    {
        var client = CreateClient();
        QueryKey key = ["select-removed"];
        client.SetQueryData(key, new Box(1));
        var options = new QueryObserverOptions<Box, Box>
        {
            QueryKey = key,
            Enabled = false,
            Select = _ => throw new InvalidOperationException("selector error"),
        };
        var observer = new QueryObserver<Box, Box>(client, options);

        observer.SetOptions(options with { Select = null });

        Assert.True(observer.CurrentResult.IsError);
    }

    [Fact(Timeout = 10_000)]
    public async Task Infinite_Observer_Reports_Select_Error_And_Keeps_Page_Flags()
    {
        var client = CreateClient();
        var observer = new InfiniteQueryObserver<string, int>(client, new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["select-infinite"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 0,
            GetNextPageParam = ctx => ctx.PageParam + 1,
            Select = _ => throw new InvalidOperationException("selector error"),
        });
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = observer.Subscribe(r =>
        {
            if (r.IsError) settled.TrySetResult();
        });
        await settled.Task;

        var result = observer.CurrentResult;
        Assert.Equal("selector error", result.Error?.Message);
        Assert.Null(result.Data);
        Assert.True(result.IsLoadingError);
        Assert.True(result.HasNextPage);
    }

    [Fact(Timeout = 10_000)]
    public async Task Infinite_Observer_Runs_Stable_Select_Once_Per_Data_Change()
    {
        var client = CreateClient();
        var runs = 0;
        var observer = new InfiniteQueryObserver<string, int>(client, new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = ["select-infinite-memo"],
            QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
            InitialPageParam = 0,
            GetNextPageParam = _ => PageParamResult<int>.None,
            Select = data => { runs++; return data; },
        });
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = observer.Subscribe(r =>
        {
            if (r.IsSuccess) settled.TrySetResult();
        });
        await settled.Task;

        _ = observer.CurrentResult;
        _ = observer.CurrentResult;

        Assert.Equal(1, runs);
    }

    private static void AssertResult<TData>(
        IQueryResult<TData> result, QueryStatus status, FetchStatus fetchStatus, TData? data)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(fetchStatus, result.FetchStatus);
        Assert.Equal(data, result.Data);
    }

    private sealed record Box(int Value);
}
