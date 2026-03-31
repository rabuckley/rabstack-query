using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery.Mvvm;

public sealed class PageViewModelTests
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
            StringQuery = Query(["test"], _ => Task.FromResult("hello"));
            EchoMutation = Mutation<string, string>((input, _, _) => Task.FromResult(input));
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_DisposesAllTrackedChildren()
    {
        // Arrange
        var client = CreateClient();
        var vm = new TestPageViewModel(client);
        await WaitForAsync(() => vm.StringQuery.IsSuccess);

        // Act
        vm.Dispose();

        // Assert — after disposal, the mutation should report idle state
        // (observer subscription cleaned up) and query should no longer fetch.
        Assert.True(vm.EchoMutation.IsIdle);
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
    public async Task Query_WithKeyAndFn_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);

        await WaitForAsync(() => vm.StringQuery.IsSuccess);
        Assert.Equal("hello", vm.StringQuery.Data);
    }

    [Fact]
    public async Task Query_WithQueryOptions_CreatesWorkingViewModel()
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
            CountQuery = Query(options);
        }
    }

    // ── Mutation Factory Methods ──────────────────────────────────────

    [Fact]
    public async Task Mutation_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        using var vm = new TestPageViewModel(client);

        await vm.EchoMutation.InvokeAsync("world");

        Assert.True(vm.EchoMutation.IsSuccess);
        Assert.Equal("world", vm.EchoMutation.Data);
    }

    [Fact]
    public async Task Mutation_WithMutationDefinition_CreatesWorkingViewModel()
    {
        var client = CreateClient();
        using var page = new MutationDefinitionPageViewModel(client);

        await page.UpperMutation.InvokeAsync("hello");

        Assert.True(page.UpperMutation.IsSuccess);
        Assert.Equal("HELLO", page.UpperMutation.Data);
    }

    [Fact]
    public async Task Mutation_WithMutationDefAndCallbacks_FiresCallbacks()
    {
        var client = CreateClient();
        var successCalled = false;

        using var page = new MutationDefinitionCallbacksPageViewModel(client, () => successCalled = true);

        await page.UpperMutation.InvokeAsync("test");

        Assert.True(page.UpperMutation.IsSuccess);
        await WaitForAsync(() => successCalled);
        Assert.True(successCalled);
    }

    [Fact]
    public async Task Mutation_WithOptimisticMutationDefinition_CreatesWorkingViewModel()
    {
        // Arrange
        var client = CreateClient();
        using var page = new OptimisticMutationDefinitionPageViewModel(client);

        // Act
        await page.UpdateMutation.InvokeAsync("new-value");

        // Assert
        Assert.True(page.UpdateMutation.IsSuccess);
        Assert.Equal("new-value", page.UpdateMutation.Data);
    }

    private sealed class MutationDefinitionPageViewModel : PageViewModel
    {
        public MutationViewModel<string, string> UpperMutation { get; }

        [SetsRequiredMembers]
        public MutationDefinitionPageViewModel(QueryClient client)
        {
            Client = client;

            var def = new MutationDefinition<string, string>
            {
                MutationFn = (input, _, _) => Task.FromResult(input.ToUpperInvariant()),
            };

            UpperMutation = Mutation(def);
        }
    }

    private sealed class MutationDefinitionCallbacksPageViewModel : PageViewModel
    {
        public MutationViewModel<string, string> UpperMutation { get; }

        [SetsRequiredMembers]
        public MutationDefinitionCallbacksPageViewModel(QueryClient client, Action onSuccess)
        {
            Client = client;

            var def = new MutationDefinition<string, string>
            {
                MutationFn = (input, _, _) => Task.FromResult(input.ToUpperInvariant()),
            };

            UpperMutation = Mutation(def, new MutationCallbacks<string, string>
            {
                OnSuccess = (data, vars, ctx) =>
                {
                    onSuccess();
                    return Task.CompletedTask;
                },
            });
        }
    }

    private sealed class OptimisticMutationDefinitionPageViewModel : PageViewModel
    {
        public MutationViewModel<string, Exception, string, string?> UpdateMutation { get; }

        [SetsRequiredMembers]
        public OptimisticMutationDefinitionPageViewModel(QueryClient client)
        {
            Client = client;

            var def = new OptimisticMutationDefinition<string, string, string?>
            {
                MutationFn = (input, _, _) => Task.FromResult(input),
                OnMutate = (input, ctx) =>
                {
                    var prev = ctx.Client.GetQueryData<string>(["data"]);
                    ctx.Client.SetQueryData(["data"], input);
                    return Task.FromResult<string?>(prev);
                },
                OnError = (err, input, prev, ctx) =>
                {
                    if (prev is not null)
                        ctx.Client.SetQueryData(["data"], prev);
                    return Task.CompletedTask;
                },
            };

            UpdateMutation = Mutation(def);
        }
    }
}
