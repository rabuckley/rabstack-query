namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for <see cref="InfiniteQueryViewModel{TData,TPageParam}"/>
/// covering property change notifications, commands, and disposal.
/// </summary>
public sealed class InfiniteQueryViewModelTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    /// <summary>
    /// Waits for the ViewModel's IsSuccess property to become true via
    /// PropertyChanged, avoiding hardcoded Task.Delay.
    /// </summary>
    private static async Task WaitForSuccess<TData, TPageParam>(
        InfiniteQueryViewModel<TData, TPageParam> vm)
    {
        if (vm.IsSuccess) return;

        var tcs = new TaskCompletionSource<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsSuccess) && vm.IsSuccess)
                tcs.TrySetResult(true);
        };

        // Check again after subscribing to avoid race
        if (vm.IsSuccess) tcs.TrySetResult(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Should_Propagate_Data_And_Success_State()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-success"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 0,
                GetNextPageParam = ctx => ctx.PageParam + 1,
            });

        // Act — wait for initial fetch
        await WaitForSuccess(vm);

        // Assert
        Assert.True(vm.IsSuccess);
        Assert.NotNull(vm.Data);
        Assert.Single(vm.Data.Pages);
        Assert.Equal("page-0", vm.Data.Pages[0]);
    }

    [Fact]
    public async Task HasNextPage_Should_Update_After_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-has-next"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 0,
                GetNextPageParam = ctx => ctx.PageParam < 2
                    ? ctx.PageParam + 1
                    : PageParamResult<int>.None,
            });

        await WaitForSuccess(vm);

        // Assert — should have next page initially
        Assert.True(vm.HasNextPage);

        // Act — fetch until no more pages
        await vm.FetchNextPageCommand.ExecuteAsync(null);
        await vm.FetchNextPageCommand.ExecuteAsync(null);

        // Assert — no more pages
        Assert.False(vm.HasNextPage);
    }

    [Fact]
    public async Task FetchNextPageCommand_Should_Add_Pages()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-fetch-next"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 0,
                GetNextPageParam = ctx => ctx.PageParam + 1,
            });

        await WaitForSuccess(vm);
        Assert.Single(vm.Data!.Pages);

        // Act
        await vm.FetchNextPageCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, vm.Data!.Pages.Count);
        Assert.Equal("page-1", vm.Data.Pages[1]);
    }

    [Fact]
    public async Task FetchPreviousPageCommand_Should_Prepend_Pages()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-fetch-prev"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 5,
                GetNextPageParam = ctx => ctx.PageParam + 1,
                GetPreviousPageParam = ctx => ctx.PageParam > 0
                    ? ctx.PageParam - 1
                    : PageParamResult<int>.None,
            });

        await WaitForSuccess(vm);
        Assert.Single(vm.Data!.Pages);

        // Act
        await vm.FetchPreviousPageCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, vm.Data!.Pages.Count);
        Assert.Equal("page-4", vm.Data.Pages[0]); // prepended
        Assert.Equal("page-5", vm.Data.Pages[1]); // original
    }

    [Fact]
    public async Task Error_State_Should_Propagate()
    {
        // Arrange
        var client = CreateQueryClient();
        var callCount = 0;

        using var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-error"],
                QueryFn = ctx =>
                {
                    callCount++;
                    if (callCount > 1)
                        throw new InvalidOperationException("page fetch failed");
                    return Task.FromResult($"page-{ctx.PageParam}");
                },
                InitialPageParam = 0,
                GetNextPageParam = ctx => ctx.PageParam + 1,
            });

        await WaitForSuccess(vm);
        Assert.True(vm.IsSuccess);

        // Act — fetch next page which will fail
        await vm.FetchNextPageCommand.ExecuteAsync(null);

        // Assert
        Assert.True(vm.IsError);
        Assert.IsType<InvalidOperationException>(vm.Error);
        Assert.True(vm.IsFetchNextPageError);
    }

    [Fact]
    public async Task Dispose_Should_Clean_Up_Subscription()
    {
        // Arrange
        var client = CreateQueryClient();
        var vm = new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["vm-dispose"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 0,
                GetNextPageParam = _ => PageParamResult<int>.None,
            });

        await WaitForSuccess(vm);

        // Act — should not throw
        vm.Dispose();

        // Assert — no exception, just verifying clean disposal
        Assert.True(vm.IsSuccess); // retains last state
    }
}
