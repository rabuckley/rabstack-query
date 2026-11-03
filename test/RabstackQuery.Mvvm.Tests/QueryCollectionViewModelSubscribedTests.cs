using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

public sealed class QueryCollectionViewModelSubscribedTests : SyncContextTestBase
{
    private static void Replace(IEnumerable<string>? data, ObservableCollection<string> items)
    {
        items.Clear();
        foreach (var item in data ?? []) items.Add(item);
    }

    // TanStack useQuery.test.tsx: "should not be attached to the query when subscribed is false".
    [Fact]
    public void Constructed_Unsubscribed_Should_Show_Cached_Items_Without_Attaching_Or_Fetching()
    {
        var client = new QueryClient(new QueryCache());
        client.SetQueryData<IEnumerable<string>>(["detached"], ["cached"]);
        var fetchCount = 0;

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["detached"],
                QueryFn = _ =>
                {
                    fetchCount++;
                    return Task.FromResult<IEnumerable<string>>(["fetched"]);
                },
            },
            Replace,
            subscribed: false);

        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["detached"] });
        Assert.NotNull(query);
        Assert.False(vm.Subscribed);
        Assert.Equal(["cached"], vm.Items);
        Assert.Equal(0, query.ObserverCount);
        Assert.Equal(0, fetchCount);
    }

    [Fact]
    public void UseQueryCollection_Unsubscribed_Should_Not_Attach_Until_Subscribed()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = client.UseQueryCollection<string, string>(
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["factory"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["fetched"]),
            },
            Replace,
            subscribed: false);
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["factory"] });
        Assert.NotNull(query);
        var observersWhileDetached = query.ObserverCount;

        vm.Subscribed = true;

        Assert.Equal(0, observersWhileDetached);
        Assert.Equal(1, query.ObserverCount);
    }

    [Fact]
    public async Task Subscribed_True_Should_Bring_Items_Up_To_Date_With_Cache()
    {
        var client = new QueryClient(new QueryCache());
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["items"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["fetched"]),
                StaleTime = TimeSpan.FromHours(1),
            },
            Replace);
        vm.Items.CollectionChanged += (_, _) =>
        {
            if (vm.Items.Contains("fetched")) fetched.TrySetResult();
        };
        if (!vm.Items.Contains("fetched"))
            await fetched.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["items"] });
        Assert.NotNull(query);

        vm.Subscribed = false;
        var observersWhileUnsubscribed = query.ObserverCount;
        client.SetQueryData<IEnumerable<string>>(["items"], ["updated"]);
        var itemsWhileUnsubscribed = vm.Items.ToList();
        vm.Subscribed = true;

        Assert.Equal(0, observersWhileUnsubscribed);
        Assert.Equal(["fetched"], itemsWhileUnsubscribed);
        Assert.Equal(1, query.ObserverCount);
        Assert.Equal(["updated"], vm.Items);
    }

    [Fact]
    public void Subscribed_Should_Raise_PropertyChanged_Only_When_Value_Changes()
    {
        var client = new QueryClient(new QueryCache());
        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["notify"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["item"]),
            },
            Replace);
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Subscribed)) changes++;
        };

        vm.Subscribed = true;
        vm.Subscribed = false;
        vm.Subscribed = false;

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Subscribed_True_After_Dispose_Should_Throw()
    {
        var client = new QueryClient(new QueryCache());
        var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["disposed"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["item"]),
            },
            Replace);
        vm.Dispose();

        void Act() => vm.Subscribed = true;

        Assert.Throws<ObjectDisposedException>(Act);
    }
}
