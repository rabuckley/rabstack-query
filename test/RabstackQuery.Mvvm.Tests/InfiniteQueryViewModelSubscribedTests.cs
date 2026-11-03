using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery.Mvvm;

public sealed class InfiniteQueryViewModelSubscribedTests : SyncContextTestBase
{
    private static InfiniteQueryObserverOptions<string, int> Options(QueryKey key, TimeSpan? gcTime = null) => new()
    {
        QueryKey = key,
        QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
        InitialPageParam = 0,
        GetNextPageParam = _ => PageParamResult<int>.None,
        GcTime = gcTime,
    };

    // TanStack useQuery.test.tsx: "should not be attached to the query when subscribed is false".
    [Fact]
    public void Constructed_Unsubscribed_Should_Not_Attach_Or_Fetch_Until_Subscribed()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new InfiniteQueryViewModel<string, int>(client, Options(["detached"]), subscribed: false);
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["detached"] });
        Assert.NotNull(query);
        var observersWhileDetached = query.ObserverCount;
        var fetchStatusWhileDetached = vm.FetchStatus;

        vm.Subscribed = true;

        Assert.Equal(0, observersWhileDetached);
        Assert.Equal(FetchStatus.Idle, fetchStatusWhileDetached);
        Assert.Equal(1, query.ObserverCount);
        Assert.True(vm.Subscribed);
    }

    [Fact]
    public void UseInfiniteQuery_Unsubscribed_Should_Not_Attach()
    {
        var client = new QueryClient(new QueryCache());

        using var vm = client.UseInfiniteQuery(Options(["factory"]), subscribed: false);

        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["factory"] });
        Assert.NotNull(query);
        Assert.False(vm.Subscribed);
        Assert.Equal(0, query.ObserverCount);
        Assert.Equal(FetchStatus.Idle, vm.FetchStatus);
    }

    [Fact]
    public void Subscribed_False_Then_True_Should_Detach_And_Reattach_Observer()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new InfiniteQueryViewModel<string, int>(client, Options(["toggle"]));
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["toggle"] });
        Assert.NotNull(query);

        vm.Subscribed = false;
        var whileUnsubscribed = query.ObserverCount;
        vm.Subscribed = true;

        Assert.Equal(0, whileUnsubscribed);
        Assert.Equal(1, query.ObserverCount);
    }

    [Fact]
    public async Task Subscribed_True_After_Eviction_Should_Fetch_Pages_Again()
    {
        var timeProvider = new FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);
        using var vm = new InfiniteQueryViewModel<string, int>(client, Options(["evicted"], TimeSpan.FromMinutes(1)));
        await WaitForAsync(vm, () => vm.IsSuccess);
        vm.Subscribed = false;
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(client.QueryCache.Find(new QueryFilters { QueryKey = ["evicted"] }));

        vm.Subscribed = true;

        await WaitForAsync(vm, () => vm.IsSuccess && vm.Data is { Pages.Count: 1 });
        Assert.Equal("page-0", vm.Data?.Pages[0]);
        Assert.NotNull(client.QueryCache.Find(new QueryFilters { QueryKey = ["evicted"] }));
    }

    [Fact]
    public void Subscribed_True_After_Dispose_Should_Throw()
    {
        var client = new QueryClient(new QueryCache());
        var vm = new InfiniteQueryViewModel<string, int>(client, Options(["disposed"]));
        vm.Dispose();

        void Act() => vm.Subscribed = true;

        Assert.Throws<ObjectDisposedException>(Act);
    }

    private static async Task WaitForAsync(
        InfiniteQueryViewModel<string, int> vm, Func<bool> condition)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? _, System.ComponentModel.PropertyChangedEventArgs __)
        {
            if (condition()) tcs.TrySetResult();
        }

        vm.PropertyChanged += OnChanged;
        try
        {
            OnChanged(null, null!);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            vm.PropertyChanged -= OnChanged;
        }
    }
}
