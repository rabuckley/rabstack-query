using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery.Mvvm;

/// <summary>
/// <c>Subscribed</c> ports the option from TanStack's <c>useBaseQuery</c>, which its
/// React Native guide pairs with screen focus (docs/framework/react/react-native.md,
/// "Disable queries on out of focus screens").
/// </summary>
public sealed class QueryViewModelSubscribedTests : SyncContextTestBase
{
    [Fact]
    public void Subscribed_False_Should_Remove_Observer_From_Query()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new QueryViewModel<string>(client, ["detach"], _ => Task.FromResult("data"));
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["detach"] });
        Assert.NotNull(query);
        Assert.Equal(1, query.ObserverCount);

        vm.Subscribed = false;

        Assert.Equal(0, query.ObserverCount);
    }

    [Fact]
    public async Task Subscribed_True_Should_Reattach_Refetch_When_Stale_And_Apply_Result()
    {
        var client = new QueryClient(new QueryCache());
        var fetchCount = 0;
        var secondFetch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = new QueryViewModel<string>(client, ["resume"], _ =>
            Interlocked.Increment(ref fetchCount) == 1 ? Task.FromResult("first") : secondFetch.Task);
        await WaitForDataAsync(vm, "first");
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["resume"] });
        Assert.NotNull(query);
        vm.Subscribed = false;

        vm.Subscribed = true;

        Assert.True(vm.Subscribed);
        Assert.Equal(1, query.ObserverCount);
        Assert.Equal(2, Volatile.Read(ref fetchCount));
        Assert.True(vm.IsFetching);

        secondFetch.SetResult("second");
        await WaitForDataAsync(vm, "second");
    }

    [Fact]
    public async Task Subscribed_True_Should_Show_Cache_Updates_Made_While_Unsubscribed()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new QueryViewModel<string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["catch-up"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.FromHours(1),
        });
        await WaitForDataAsync(vm, "fetched");
        vm.Subscribed = false;
        client.SetQueryData(["catch-up"], "updated while hidden");
        Assert.Equal("fetched", vm.Data);

        vm.Subscribed = true;

        Assert.Equal("updated while hidden", vm.Data);
    }

    [Fact]
    public async Task Update_Posted_Before_Unsubscribing_Should_Not_Overwrite_Result_Applied_On_Resume()
    {
        // Disappearing and Appearing can both run ahead of a queued UI post, which then
        // carries a result older than the one the resume just applied.
        var syncContext = new DeferredSynchronizationContext();
        var client = new QueryClient(new QueryCache());
        var vm = WithSyncContext(syncContext, () => new QueryViewModel<string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["stale-post"],
            QueryFn = _ => new TaskCompletionSource<string>().Task,
            Enabled = false,
        }));
        syncContext.DrainQueue();
        client.SetQueryData(["stale-post"], "old");
        Assert.True(syncContext.QueuedCount > 0);
        vm.Subscribed = false;
        client.SetQueryData(["stale-post"], "new");

        vm.Subscribed = true;
        Assert.Equal("new", vm.Data);
        syncContext.DrainQueue();

        Assert.Equal("new", vm.Data);
        vm.Dispose();
    }

    [Fact]
    public async Task Subscribed_True_Should_Attach_To_Cached_Query_After_Eviction()
    {
        var timeProvider = new FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);
        using var vm = new QueryViewModel<string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["evicted"],
            QueryFn = _ => Task.FromResult("data"),
            GcTime = TimeSpan.FromMinutes(1),
        });
        await WaitForDataAsync(vm, "data");
        var original = client.QueryCache.Find(new QueryFilters { QueryKey = ["evicted"] });
        vm.Subscribed = false;
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(client.QueryCache.Find(new QueryFilters { QueryKey = ["evicted"] }));

        vm.Subscribed = true;

        var current = client.QueryCache.Find(new QueryFilters { QueryKey = ["evicted"] });
        Assert.NotNull(current);
        Assert.NotSame(original, current);
        Assert.Equal(1, current.ObserverCount);
    }

    [Fact]
    public void Subscribed_Should_Raise_PropertyChanged_Only_When_Value_Changes()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new QueryViewModel<string>(client, ["notify"], _ => Task.FromResult("data"));
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Subscribed)) changes++;
        };

        vm.Subscribed = true;
        vm.Subscribed = false;
        vm.Subscribed = false;

        Assert.False(vm.Subscribed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Subscribed_True_After_Dispose_Should_Throw()
    {
        var client = new QueryClient(new QueryCache());
        var vm = new QueryViewModel<string>(client, ["disposed"], _ => Task.FromResult("data"));
        vm.Dispose();

        void Act() => vm.Subscribed = true;

        Assert.Throws<ObjectDisposedException>(Act);
    }

    [Fact]
    public void Subscribed_False_After_Dispose_Should_Do_Nothing()
    {
        // A late Disappearing on a replaced ViewModel must not throw from a UI event handler.
        var client = new QueryClient(new QueryCache());
        var vm = new QueryViewModel<string>(client, ["disposed-false"], _ => Task.FromResult("data"));
        vm.Dispose();

        vm.Subscribed = false;

        Assert.False(vm.Subscribed);
    }

    // TanStack's useBaseQuery reads getOptimisticResult on every render, so subscribed: false still
    // shows the result for the current options. See the comment in QueryViewModel.SetOptions.
    [Fact]
    public void SetOptions_After_Pausing_Should_Show_New_Key()
    {
        var client = new QueryClient(new QueryCache());
        client.SetQueryData(["paused-before"], "before");
        client.SetQueryData(["paused-after"], "after");
        using var vm = new QueryViewModel<string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["paused-before"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.FromHours(1),
        });
        vm.Subscribed = false;

        vm.SetOptions(new QueryObserverOptions<string>
        {
            QueryKey = ["paused-after"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.FromHours(1),
        });

        Assert.Equal("after", vm.Data);
    }

    [Fact]
    public void SetOptions_After_Dispose_Should_Not_Change_State()
    {
        var client = new QueryClient(new QueryCache());
        client.SetQueryData(["disposed-before"], "before");
        client.SetQueryData(["disposed-after"], "after");
        var vm = new QueryViewModel<string>(client, new QueryObserverOptions<string>
        {
            QueryKey = ["disposed-before"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.FromHours(1),
        });
        vm.Dispose();

        vm.SetOptions(new QueryObserverOptions<string>
        {
            QueryKey = ["disposed-after"],
            QueryFn = _ => Task.FromResult("fetched"),
            StaleTime = TimeSpan.FromHours(1),
        });

        Assert.Equal("before", vm.Data);
    }

    private static async Task WaitForDataAsync(QueryViewModel<string> vm, string expected)
    {
        var deadline = TimeSpan.FromSeconds(5);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? _, System.ComponentModel.PropertyChangedEventArgs __)
        {
            if (vm.Data == expected) tcs.TrySetResult();
        }

        vm.PropertyChanged += OnChanged;
        try
        {
            OnChanged(null, null!);
            await tcs.Task.WaitAsync(deadline, TestContext.Current.CancellationToken);
        }
        finally
        {
            vm.PropertyChanged -= OnChanged;
        }
    }
}
