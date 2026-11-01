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
            EchoMutation = UseMutation<string, string>((input, _, _) => Task.FromResult(input));
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
                MutationFn = (input, _, _) => Task.FromResult(input.ToUpperInvariant()),
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
                    (string input, MutationFunctionContext _, CancellationToken _) =>
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
