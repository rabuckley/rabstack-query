using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

public sealed class ConsumerCallShapeTests : SyncContextTestBase
{
    private sealed record Item(int Id, string Name);

    private sealed class ItemViewModel
    {
        public ItemViewModel(Item item) => Item = item;

        public Item Item { get; set; }
    }

    private static QueryClient CreateClient() => new(new QueryCache());

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(10);
        }
    }

    private static QueryOptions<Item> ItemQuery(int id) => new()
    {
        QueryKey = ["items", id],
        QueryFn = _ => Task.FromResult(new Item(id, $"item-{id}")),
        StaleTime = TimeSpan.FromMinutes(1),
    };

    [Fact]
    public async Task SimpleQuery_InfersDataTypeFromQueryFn()
    {
        var client = CreateClient();

        using QueryViewModel<int> vm = client.UseQuery(["count"], _ => Task.FromResult(42));

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(42, vm.Data);
    }

    [Fact]
    public async Task ReusableQueryWithSelect_KeepsSourceTypeInCache()
    {
        var client = CreateClient();

        using QueryViewModel<string, Item> vm = client.UseQuery(ItemQuery(1), item => item.Name);

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal("item-1", vm.Data);
        Assert.Equal(new Item(1, "item-1"), client.GetQueryData(ItemQuery(1)));
    }

    [Fact]
    public void ReusableQueryWithObserverOverrides_SharesDefinitionCacheEntry()
    {
        var client = CreateClient();
        client.SetQueryData(ItemQuery(2), new Item(2, "seeded"));

        // Disabled so the observer cannot fetch; it can only see data through the shared cache entry.
        using QueryViewModel<Item> vm = client.UseQuery(
            ItemQuery(2).ToObserverOptions() with
            {
                Enabled = false,
            });

        Assert.Equal(new Item(2, "seeded"), vm.Data);
        Assert.False(vm.IsStale);
    }

    [Fact]
    public async Task ReusableQueryWithSelectAndOverrides_UsesSelectedOutput()
    {
        var client = CreateClient();
        var release = new TaskCompletionSource<Item>(TaskCreationOptions.RunContinuationsAsynchronously);
        var definition = QueryOptions.Create(["gated-item"], _ => release.Task);

        using QueryViewModel<string, Item> vm = client.UseQuery(
            definition.ToObserverOptions(item => item.Name) with
            {
                PlaceholderData = (_, _) => new Item(3, "placeholder"),
            });

        Assert.True(vm.IsPlaceholderData);
        Assert.Equal("placeholder", vm.Data);

        release.SetResult(new Item(3, "item-3"));
        await WaitForAsync(() => vm.IsSuccess && !vm.IsPlaceholderData);
        Assert.Equal("item-3", vm.Data);
    }

    [Fact]
    public async Task SameTypeCollection_InfersItemType_WithUntypedUpdate()
    {
        var client = CreateClient();
        var options = QueryOptions.Create<IEnumerable<string>>(["names"], _ => Task.FromResult<IEnumerable<string>>(["a", "b"]));

        using QueryCollectionViewModel<string, string> vm = client.UseQueryCollection(
            options,
            update: (data, items) =>
            {
                items.Clear();
                foreach (var item in data ?? []) items.Add(item);
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(["a", "b"], vm.Items);
    }

    [Fact]
    public async Task ModelToViewModelCollection_ReceivesRawItems_AndKeepsExistingItemIdentity()
    {
        var client = CreateClient();
        var version = 0;
        var options = new QueryOptions<IEnumerable<Item>>
        {
            QueryKey = ["item-list"],
            QueryFn = _ => Task.FromResult<IEnumerable<Item>>([new Item(1, $"v{Interlocked.Increment(ref version)}")]),
        };

        using QueryCollectionViewModel<ItemViewModel, Item> vm = client.UseQueryCollection<Item, ItemViewModel>(
            options,
            update: (data, items) => Reconcile(data, items));

        await WaitForAsync(() => vm.Items.Count == 1);
        var first = vm.Items[0];
        await vm.RefetchCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.Items[0].Item.Name == "v2");

        Assert.Same(first, vm.Items[0]);

        static void Reconcile(IEnumerable<Item>? data, ObservableCollection<ItemViewModel> items)
        {
            foreach (var item in data ?? [])
            {
                if (items.FirstOrDefault(vm => vm.Item.Id == item.Id) is { } existing)
                    existing.Item = item;
                else
                    items.Add(new ItemViewModel(item));
            }
        }
    }

    [Fact]
    public async Task SimpleMutation_ReturnsShortViewModel()
    {
        var client = CreateClient();

        using MutationViewModel<string, int> vm = client.UseMutation(
            MutationOptions.Create((int id, MutationFunctionContext _, CancellationToken _) =>
                Task.FromResult($"saved-{id}")));

        Assert.Equal("saved-4", await vm.InvokeAsync(4));
    }

    [Fact]
    public async Task OptimisticMutation_InfersRollbackType()
    {
        var client = CreateClient();
        var name = "before";

        using MutationViewModel<string, Exception, string, string> vm = client.UseMutation(
            MutationOptions.Create(
                (string next, MutationFunctionContext _, CancellationToken _) => Task.FromResult(next),
                onMutate: (next, _) =>
                {
                    var previous = name;
                    name = next;
                    return Task.FromResult(previous);
                }));

        Assert.Equal("after", await vm.InvokeAsync("after"));
        Assert.Equal("after", name);
    }
}
