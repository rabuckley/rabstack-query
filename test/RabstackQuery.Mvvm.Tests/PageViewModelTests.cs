using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery.Mvvm;

public sealed class PageViewModelTests : SyncContextTestBase
{
    private static QueryClient CreateClient() => new(new QueryCache());

    private static async Task WaitForAsync(
        Func<bool> condition,
        int timeoutMs = 5_000,
        int pollIntervalMs = 10)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(pollIntervalMs);
        }
    }

    // ── Test ViewModel ────────────────────────────────────────────────

    private sealed class TestPageViewModel : PageViewModel
    {
        public QueryViewModel<string> StringQuery { get; }
        public MutationViewModel<string, string> EchoMutation { get; }

        [SetsRequiredMembers]
        public TestPageViewModel(QueryClient client)
        {
            Client = client;
            StringQuery = UseQuery(["test"], _ => Task.FromResult("hello"));
            EchoMutation = UseMutation<string, string>((input, _) => Task.FromResult(input));
        }

        public QueryViewModel<string> AddLateQuery() =>
            UseQuery(["late"], _ => Task.FromResult("late"));
    }

    private sealed class HiddenPageViewModel : PageViewModel
    {
        private static readonly QueryKey Key = ["hidden"];
        private int _fetchCount;

        [SetsRequiredMembers]
        public HiddenPageViewModel(QueryClient client) => Client = client;

        public int FetchCount => Volatile.Read(ref _fetchCount);

        public object Create(string factory) => factory switch
        {
            nameof(QueryFromKey) => QueryFromKey(),
            nameof(QueryFromOptions) => QueryFromOptions(),
            nameof(QueryFromOptionsWithSelect) => QueryFromOptionsWithSelect(),
            nameof(QueryFromObserverOptions) => QueryFromObserverOptions(),
            nameof(QueryFromSelectingObserverOptions) => QueryFromSelectingObserverOptions(),
            nameof(CollectionFromKey) => CollectionFromKey(),
            nameof(MappedCollectionFromKey) => MappedCollectionFromKey(),
            nameof(CollectionFromOptions) => CollectionFromOptions(),
            nameof(MappedCollectionFromOptions) => MappedCollectionFromOptions(),
            nameof(CollectionFromObserverOptions) => CollectionFromObserverOptions(),
            nameof(InfiniteQuery) => InfiniteQuery(),
            _ => throw new ArgumentOutOfRangeException(nameof(factory)),
        };

        public QueryViewModel<string> QueryFromKey() => UseQuery(Key, FetchAsync);

        public QueryViewModel<string> QueryFromOptions() =>
            UseQuery(new QueryOptions<string> { QueryKey = Key, QueryFn = FetchAsync });

        public QueryViewModel<int, string> QueryFromOptionsWithSelect() =>
            UseQuery(new QueryOptions<string> { QueryKey = Key, QueryFn = FetchAsync }, data => data.Length);

        public QueryViewModel<string> QueryFromObserverOptions() =>
            UseQuery(new QueryObserverOptions<string> { QueryKey = Key, QueryFn = FetchAsync });

        public QueryViewModel<int, string> QueryFromSelectingObserverOptions() =>
            UseQuery(new QueryObserverOptions<int, string>
            {
                QueryKey = Key,
                QueryFn = FetchAsync,
                Select = data => data.Length,
            });

        public QueryCollectionViewModel<string, string> CollectionFromKey() =>
            UseQueryCollection<string>(Key, FetchItemsAsync, ReplaceItems);

        public QueryCollectionViewModel<string, string> MappedCollectionFromKey() =>
            UseQueryCollection<string, string>(Key, FetchItemsAsync, ReplaceItems);

        public QueryCollectionViewModel<string, string> CollectionFromOptions() =>
            UseQueryCollection<string>(
                new QueryOptions<IEnumerable<string>> { QueryKey = Key, QueryFn = FetchItemsAsync },
                ReplaceItems);

        public QueryCollectionViewModel<string, string> MappedCollectionFromOptions() =>
            UseQueryCollection<string, string>(
                new QueryOptions<IEnumerable<string>> { QueryKey = Key, QueryFn = FetchItemsAsync },
                ReplaceItems);

        public QueryCollectionViewModel<string, string> CollectionFromObserverOptions() =>
            UseQueryCollection<string, string>(
                new QueryObserverOptions<IEnumerable<string>> { QueryKey = Key, QueryFn = FetchItemsAsync },
                ReplaceItems);

        public InfiniteQueryViewModel<string, int> InfiniteQuery() =>
            UseInfiniteQuery(new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = Key,
                QueryFn = _ =>
                {
                    Interlocked.Increment(ref _fetchCount);
                    return Task.FromResult("page");
                },
                InitialPageParam = 0,
                GetNextPageParam = _ => PageParamResult<int>.None,
            });

        private Task<string> FetchAsync(QueryFunctionContext _)
        {
            Interlocked.Increment(ref _fetchCount);
            return Task.FromResult("fetched");
        }

        private Task<IEnumerable<string>> FetchItemsAsync(QueryFunctionContext _)
        {
            Interlocked.Increment(ref _fetchCount);
            return Task.FromResult<IEnumerable<string>>(["fetched"]);
        }

        private static void ReplaceItems(IEnumerable<string>? data, ObservableCollection<string> items)
        {
            items.Clear();
            foreach (var item in data ?? []) items.Add(item);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_ReleasesOwnedQueryAndMutationObservers()
    {
        // Arrange
        var client = CreateClient();
        var vm = new TestPageViewModel(client);
        await WaitForAsync(() => vm.StringQuery.IsSuccess);
        await vm.EchoMutation.InvokeAsync("x");
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["test"] });
        var mutation = Assert.Single(client.MutationCache.GetAll());
        Assert.NotNull(query);
        Assert.Equal(1, query.ObserverCount);
        Assert.True(mutation.HasObservers);

        // Act
        vm.Dispose();

        // Assert
        Assert.Equal(0, query.ObserverCount);
        Assert.False(mutation.HasObservers);
    }

    [Fact]
    public void DoubleDispose_IsSafe()
    {
        var client = CreateClient();
        var vm = new TestPageViewModel(client);

        // Act — should not throw
        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public async Task Subscribed_False_Then_True_DetachesAndReattachesOwnedObservers()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);
        await WaitForAsync(() => vm.StringQuery.IsSuccess);
        await vm.EchoMutation.InvokeAsync("x");
        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["test"] });
        var mutation = Assert.Single(client.MutationCache.GetAll());
        Assert.NotNull(query);
        Assert.True(vm.Subscribed);

        vm.Subscribed = false;
        var queryObserversWhileUnsubscribed = query.ObserverCount;
        var mutationObservedWhileUnsubscribed = mutation.HasObservers;
        vm.Subscribed = true;

        Assert.Equal(0, queryObserversWhileUnsubscribed);
        Assert.False(mutationObservedWhileUnsubscribed);
        Assert.Equal(1, query.ObserverCount);
        Assert.True(mutation.HasObservers);
    }

    [Fact]
    public void Subscribed_False_AppliesToViewModelsCreatedWhileUnsubscribed()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);
        vm.Subscribed = false;

        var late = vm.AddLateQuery();

        var query = client.QueryCache.Find(new QueryFilters { QueryKey = ["late"] });
        Assert.NotNull(query);
        Assert.False(late.Subscribed);
        Assert.Equal(0, query.ObserverCount);

        vm.Subscribed = true;

        Assert.True(late.Subscribed);
        Assert.Equal(1, query.ObserverCount);
    }

    // TanStack useQuery.test.tsx: "should not be attached to the query when subscribed is false".
    [Theory]
    [InlineData(nameof(HiddenPageViewModel.QueryFromKey))]
    [InlineData(nameof(HiddenPageViewModel.QueryFromOptions))]
    [InlineData(nameof(HiddenPageViewModel.QueryFromOptionsWithSelect))]
    [InlineData(nameof(HiddenPageViewModel.QueryFromObserverOptions))]
    [InlineData(nameof(HiddenPageViewModel.QueryFromSelectingObserverOptions))]
    [InlineData(nameof(HiddenPageViewModel.CollectionFromKey))]
    [InlineData(nameof(HiddenPageViewModel.MappedCollectionFromKey))]
    [InlineData(nameof(HiddenPageViewModel.CollectionFromOptions))]
    [InlineData(nameof(HiddenPageViewModel.MappedCollectionFromOptions))]
    [InlineData(nameof(HiddenPageViewModel.CollectionFromObserverOptions))]
    [InlineData(nameof(HiddenPageViewModel.InfiniteQuery))]
    public void QueryCreatedWhileUnsubscribed_FetchesOnlyAfterResume(string factory)
    {
        var client = CreateClient();
        using var page = new HiddenPageViewModel(client);
        page.Subscribed = false;

        page.Create(factory);
        var fetchesWhileUnsubscribed = page.FetchCount;
        page.Subscribed = true;

        Assert.Equal(0, fetchesWhileUnsubscribed);
        Assert.Equal(1, page.FetchCount);
    }

    [Fact]
    public void QueryCreatedWhileUnsubscribed_ShowsCachedDataAsIdle()
    {
        var client = CreateClient();
        client.SetQueryData(["hidden"], "cached");
        using var page = new HiddenPageViewModel(client);
        page.Subscribed = false;

        var query = page.QueryFromObserverOptions();

        Assert.False(query.Subscribed);
        Assert.Equal("cached", query.Data);
        Assert.True(query.IsSuccess);
        Assert.Equal(FetchStatus.Idle, query.FetchStatus);
        Assert.Equal(0, page.FetchCount);
    }

    [Fact]
    public void CollectionCreatedWhileUnsubscribed_ShowsCachedItems()
    {
        var client = CreateClient();
        client.SetQueryData<IEnumerable<string>>(["hidden"], ["cached"]);
        using var page = new HiddenPageViewModel(client);
        page.Subscribed = false;

        var collection = page.CollectionFromObserverOptions();

        Assert.False(collection.Subscribed);
        Assert.Equal(["cached"], collection.Items);
        Assert.Equal(0, page.FetchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subscribed_AppliesToEveryChild_WhenChildCallbackCreatesViewModel(bool target)
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);
        vm.Subscribed = !target;
        QueryViewModel<string>? late = null;
        vm.StringQuery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StringQuery.Subscribed)) late = vm.AddLateQuery();
        };

        vm.Subscribed = target;

        Assert.Equal(target, vm.StringQuery.Subscribed);
        Assert.Equal(target, vm.EchoMutation.Subscribed);
        Assert.NotNull(late);
        Assert.Equal(target, late.Subscribed);
    }

    [Fact]
    public void Subscribed_LeavesChildrenConsistent_WhenChildCallbackChangesPageValue()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);
        var changes = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Subscribed)) changes.Add(vm.Subscribed);
        };
        vm.StringQuery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StringQuery.Subscribed) && !vm.StringQuery.Subscribed)
                vm.Subscribed = true;
        };

        vm.Subscribed = false;

        Assert.True(vm.Subscribed);
        Assert.True(vm.StringQuery.Subscribed);
        Assert.True(vm.EchoMutation.Subscribed);
        Assert.All(changes, Assert.True);
    }

    // Page children have no per-child opt-out: see "Pausing MVVM ViewModels" in ARCHITECTURE.md.
    [Fact]
    public void ManuallyDetachedChild_FollowsPage_WhenPageResumes()
    {
        using var vm = new TestPageViewModel(CreateClient());
        vm.StringQuery.Subscribed = false;
        vm.Subscribed = false;

        vm.Subscribed = true;

        Assert.True(vm.StringQuery.Subscribed);
    }

    [Fact]
    public void Subscribed_RaisesPropertyChangedOnlyWhenValueChanges()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);
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
    public void Subscribed_True_AfterDispose_Throws()
    {
        var client = CreateClient();
        var vm = new TestPageViewModel(client);
        vm.Dispose();

        void Act() => vm.Subscribed = true;

        Assert.Throws<ObjectDisposedException>(Act);
    }

    [Fact]
    public void Subscribed_False_AfterDispose_DoesNothing()
    {
        var client = CreateClient();
        var vm = new TestPageViewModel(client);
        vm.Dispose();

        vm.Subscribed = false;

        Assert.False(vm.Subscribed);
    }

    // ── Query Factory Methods ─────────────────────────────────────────

    [Fact]
    public async Task UseQuery_WithKeyAndFn_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);

        await WaitForAsync(() => vm.StringQuery.IsSuccess);
        Assert.Equal("hello", vm.StringQuery.Data);
    }

    [Fact]
    public async Task UseQuery_WithQueryOptions_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        var options = new QueryOptions<int>
        {
            QueryKey = ["count"],
            QueryFn = _ => Task.FromResult(42),
        };

        using var page = new QueryOptionsPageViewModel(client, options);

        await WaitForAsync(() => page.CountQuery.IsSuccess);
        Assert.Equal(42, page.CountQuery.Data);
    }

    private sealed class QueryOptionsPageViewModel : PageViewModel
    {
        public QueryViewModel<int> CountQuery { get; }

        [SetsRequiredMembers]
        public QueryOptionsPageViewModel(QueryClient client, QueryOptions<int> options)
        {
            Client = client;
            CountQuery = UseQuery(options);
        }
    }

    // ── Mutation Factory Methods ──────────────────────────────────────

    [Fact]
    public async Task UseMutation_WithFunction_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);

        await vm.EchoMutation.InvokeAsync("world");

        Assert.True(vm.EchoMutation.IsSuccess);
        Assert.Equal("world", vm.EchoMutation.Data);
    }

    [Fact]
    public async Task UseMutation_WithOptions_FiresCallbacks()
    {
        var client = CreateClient();
        var successCalled = false;

        using var page = new MutationOptionsPageViewModel(client, () => successCalled = true);

        await page.UpperMutation.InvokeAsync("test");

        Assert.True(page.UpperMutation.IsSuccess);
        Assert.Equal("TEST", page.UpperMutation.Data);
        await WaitForAsync(() => successCalled);
    }

    [Fact]
    public async Task UseMutation_WithOptimisticFactory_RollsBackCache_OnFailure()
    {
        // Arrange
        var client = CreateClient();
        client.SetQueryData(["data"], "original");
        using var page = new OptimisticMutationPageViewModel(client);

        // Act
        Task Act() => page.UpdateMutation.InvokeAsync("new-value");

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Equal("original", client.GetQueryData<string>(["data"]));
    }

    private sealed class MutationOptionsPageViewModel : PageViewModel
    {
        public MutationViewModel<string, string> UpperMutation { get; }

        [SetsRequiredMembers]
        public MutationOptionsPageViewModel(QueryClient client, Action onSuccess)
        {
            Client = client;

            UpperMutation = UseMutation(new MutationOptions<string, string>
            {
                MutationFn = (input, _) => Task.FromResult(input.ToUpperInvariant()),
                OnSuccess = (data, vars, _, ctx) =>
                {
                    onSuccess();
                    return Task.CompletedTask;
                },
            });
        }
    }

    private sealed class OptimisticMutationPageViewModel : PageViewModel
    {
        public MutationViewModel<string, Exception, string, string?> UpdateMutation { get; }

        [SetsRequiredMembers]
        public OptimisticMutationPageViewModel(QueryClient client)
        {
            Client = client;

            UpdateMutation = UseMutation(
                MutationOptions.Create(
                    (string input, MutationFunctionContext _) =>
                        Task.FromException<string>(new InvalidOperationException("rejected")),
                    onMutate: (input, ctx) =>
                    {
                        var prev = ctx.Client.GetQueryData<string>(["data"]);
                        ctx.Client.SetQueryData(["data"], input);
                        return Task.FromResult(prev);
                    })
                with
                {
                    OnError = (err, input, prev, ctx) =>
                    {
                        if (prev is not null)
                            ctx.Client.SetQueryData(["data"], prev);
                        return Task.CompletedTask;
                    },
                });
        }
    }
}
