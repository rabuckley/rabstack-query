using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for <see cref="QueryClientExtensions"/> overloads that are NOT covered by
/// <see cref="QueryViewModelOptionsTests"/>. Focuses on collection simple overloads,
/// typed collection overloads, infinite query, and mutation overloads.
/// </summary>
public sealed class QueryClientExtensionsTests
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
    public async Task UseMutation_FullOverload_Should_Create_Working_MutationViewModel()
    {
        // Tests UseMutation<TData, TError, TVariables, TOnMutateResult>(fn)
        // Arrange
        var client = CreateQueryClient();

        using var vm = client.UseMutation<string, InvalidOperationException, string, object?>(
            (input, context, ct) => Task.FromResult(input.ToUpper()));

        // Act
        await vm.MutateCommand.ExecuteAsync("hello");

        // Assert
        Assert.Equal("HELLO", vm.Data);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task UseMutation_SimplifiedOverload_Should_Default_TError_To_Exception()
    {
        // Tests UseMutation<TData, TVariables, TOnMutateResult>(fn) — TError defaults to Exception
        // Arrange
        var client = CreateQueryClient();

        using var vm = client.UseMutation<string, string, object?>(
            (input, context, ct) => Task.FromResult(input.ToUpper()));

        // Act
        await vm.MutateCommand.ExecuteAsync("world");

        // Assert
        Assert.Equal("WORLD", vm.Data);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task UseMutation_With_Options_Should_Wire_Lifecycle_Callbacks()
    {
        // Tests that lifecycle callbacks (OnSuccess, OnSettled) are properly wired
        // through the extension method.
        // Arrange
        var client = CreateQueryClient();
        var onSuccessCalled = false;
        var successData = "";

        using var vm = client.UseMutation<string, Exception, string, object?>(
            (input, context, ct) => Task.FromResult(input.ToUpper()),
            new MutationOptions<string, Exception, string, object?>
            {
                OnSuccess = (data, variables, onMutateResult, context) =>
                {
                    onSuccessCalled = true;
                    successData = data;
                    return Task.CompletedTask;
                }
            });

        // Act
        await vm.MutateCommand.ExecuteAsync("test");

        // Assert
        Assert.True(vm.IsSuccess);
        Assert.Equal("TEST", vm.Data);
        Assert.True(onSuccessCalled);
        Assert.Equal("TEST", successData);
    }

    [Fact]
    public async Task UseMutation_Without_Options_Should_Work()
    {
        // Tests that passing null/omitting options works correctly.
        // Arrange
        var client = CreateQueryClient();

        // The default parameter value for options is null
        using var vm = client.UseMutation<int, Exception, int, object?>(
            (input, context, ct) => Task.FromResult(input * 2));

        // Act
        await vm.MutateCommand.ExecuteAsync(21);

        // Assert
        Assert.Equal(42, vm.Data);
        Assert.True(vm.IsSuccess);
    }
}
