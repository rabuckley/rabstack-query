using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for <see cref="QueryClientExtensions"/> overloads that are NOT covered by
/// <see cref="QueryViewModelOptionsTests"/>. Focuses on collection simple overloads,
/// typed collection overloads, infinite query, and mutation overloads.
/// </summary>
public sealed class QueryClientExtensionsTests : SyncContextTestBase
{
    private record TodoDto(int Id, string Title);

    private class TodoItemViewModel(int id, string title)
    {
        public int Id { get; } = id;
        public string Title { get; set; } = title;
    }

    private static QueryClient CreateQueryClient() => new(new QueryCache());

    private static async Task WaitForAsync(
        Func<bool> condition,
        int timeoutMs = 5_000,
        int pollIntervalMs = 10)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException(
                    $"Condition was not satisfied within {timeoutMs}ms.");
            await Task.Delay(pollIntervalMs, TestContext.Current.CancellationToken);
        }
    }

    // ── UseQueryCollection simple overload ────────────────────────────

    [Fact]
    public async Task UseQueryCollection_SimpleOverload_Should_Create_Working_CollectionViewModel()
    {
        // Tests the UseQueryCollection<TItem>(key, fn, update) overload where
        // TItem == TQueryFnData (no type transformation).
        // Arrange
        var client = CreateQueryClient();

        // Act
        using var vm = client.UseQueryCollection<string>(
            ["simple-collection"],
            async _ => ["apple", "banana", "cherry"],
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        await WaitForAsync(() => vm.IsSuccess);

        // Assert
        Assert.Equal(3, vm.Items.Count);
        Assert.Contains("apple", vm.Items);
        Assert.Contains("banana", vm.Items);
        Assert.Contains("cherry", vm.Items);
    }

    [Fact]
    public async Task UseQueryCollection_TypedSimpleOverload_Should_Create_DifferentType_CollectionViewModel()
    {
        // Tests the UseQueryCollection<TQueryFnData, TItem>(key, fn, update) overload
        // where cache stores TodoDto but collection contains TodoItemViewModel.
        // Arrange
        var client = CreateQueryClient();

        // Act
        using var vm = client.UseQueryCollection<TodoDto, TodoItemViewModel>(
            ["typed-collection"],
            async _ =>
            [
                new TodoDto(1, "Write tests"),
                new TodoDto(2, "Ship feature")
            ],
            update: (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var dto in data)
                    items.Add(new TodoItemViewModel(dto.Id, dto.Title));
            });

        await WaitForAsync(() => vm.IsSuccess);

        // Assert
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(1, vm.Items[0].Id);
        Assert.Equal("Write tests", vm.Items[0].Title);
        Assert.Equal(2, vm.Items[1].Id);
        Assert.Equal("Ship feature", vm.Items[1].Title);
    }

    // ── UseInfiniteQuery ─────────────────────────────────────────────

    [Fact]
    public async Task UseInfiniteQuery_Should_Create_Working_InfiniteQueryViewModel()
    {
        // Tests the UseInfiniteQuery<TData, TPageParam>(options) overload.
        // Arrange
        var client = CreateQueryClient();

        // Act
        using var vm = client.UseInfiniteQuery(new InfiniteQueryObserverOptions<string[], int>
        {
            QueryKey = ["infinite-ext"],
            QueryFn = async ctx => [$"page-{ctx.PageParam}"],
            InitialPageParam = 0,
            GetNextPageParam = ctx =>
                ctx.PageParam < 2
                    ? PageParamResult<int>.Some(ctx.PageParam + 1)
                    : PageParamResult<int>.None,
        });

        await WaitForAsync(() => vm.IsSuccess);

        // Assert
        Assert.NotNull(vm.Data);
        Assert.Single(vm.Data!.Pages);
        Assert.Equal("page-0", vm.Data.Pages[0][0]);
        Assert.True(vm.HasNextPage);
    }

    // ── UseMutation overloads ────────────────────────────────────────

    [Fact]
    public async Task UseMutation_FullOptions_InfersAllFourTypeArguments()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, InvalidOperationException, string, object?>
        {
            MutationFn = (input, context, ct) => Task.FromResult(input.ToUpper()),
        };

        // Act
        using MutationViewModel<string, InvalidOperationException, string, object?> vm = client.UseMutation(options);
        await vm.MutateCommand.ExecuteAsync("hello");

        // Assert
        Assert.Equal("HELLO", vm.Data);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task UseMutation_TwoTypeOptions_ReturnsShortViewModel_AndWiresCallbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var onSuccessCalled = false;
        var successData = "";

        // Act
        using MutationViewModel<string, string> vm = client.UseMutation(new MutationOptions<string, string>
        {
            MutationFn = (input, context, ct) => Task.FromResult(input.ToUpper()),
            OnSuccess = (data, variables, onMutateResult, context) =>
            {
                onSuccessCalled = true;
                successData = data;
                return Task.CompletedTask;
            },
        });
        await vm.MutateCommand.ExecuteAsync("test");

        // Assert
        Assert.True(vm.IsSuccess);
        Assert.Equal("TEST", vm.Data);
        Assert.True(onSuccessCalled);
        Assert.Equal("TEST", successData);
    }

    [Fact]
    public async Task UseMutation_OptimisticFactory_PassesTypedRollbackState_OnFailure()
    {
        // Arrange
        var client = CreateQueryClient();
        var current = 1;
        int? rolledBackTo = null;
        var options = MutationOptions.Create(
            (int next, MutationFunctionContext _, CancellationToken _) =>
                Task.FromException<string>(new InvalidOperationException("rejected")),
            onMutate: (next, _) =>
            {
                var previous = current;
                current = next;
                return Task.FromResult(previous);
            })
            with
            {
                OnError = (_, _, previous, _) =>
                {
                    current = previous;
                    rolledBackTo = previous;
                    return Task.CompletedTask;
                },
            };

        // Act
        using MutationViewModel<string, Exception, int, int> vm = client.UseMutation(options);
        await vm.MutateCommand.ExecuteAsync(5);

        // Assert
        Assert.True(vm.IsError);
        Assert.Equal(1, rolledBackTo);
        Assert.Equal(1, current);
    }

    [Fact]
    public void UseMutation_OptionsWithoutMutationFn_ThrowsArgumentException()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, string> { Retry = 1 };

        // Act
        void Act() => client.UseMutation(options);

        // Assert
        Assert.Throws<ArgumentException>("options", Act);
    }

    [Fact]
    public void UseMutation_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var client = CreateQueryClient();
        MutationOptions<string, string>? options = null;

        // Act
        void Act() => client.UseMutation(options!);

        // Assert
        Assert.Throws<ArgumentNullException>("options", Act);
    }
}
