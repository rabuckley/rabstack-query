namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for MVVM overloads that accept <see cref="QueryOptions{TData}"/>,
/// <see cref="MutationCallbacks{TData, TVariables}"/>, and the simplified
/// ViewModel aliases (<see cref="QueryViewModel{TData}"/>,
/// <see cref="MutationViewModel{TData, TVariables}"/>).
/// </summary>
public sealed class QueryOptionsUsageTests
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

    // ── UseQuery with QueryOptions ────────────────────────────────────

    [Fact]
    public async Task UseQuery_WithQueryOptions_InfersTDataAndFetches()
    {
        var client = CreateClient();
        var options = new QueryOptions<string>
        {
            QueryKey = ["greeting"],
            QueryFn = async _ => "hello",
        };

        using var vm = client.UseQuery(options);

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal("hello", vm.Data);
    }

    [Fact]
    public async Task UseQuery_WithQueryOptionsAndSelect_AppliesTransform()
    {
        var client = CreateClient();
        var options = new QueryOptions<string>
        {
            QueryKey = ["greeting"],
            QueryFn = async _ => "hello world",
        };

        using var vm = client.UseQuery(options, s => s.Length);

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(11, vm.Data);
    }

    // ── QueryViewModel<TData> alias ───────────────────────────────────

    [Fact]
    public async Task QueryViewModelAlias_SimpleConstructor_Works()
    {
        var client = CreateClient();
        using var vm = new QueryViewModel<int>(
            client,
            ["count"],
            async _ => 42);

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(42, vm.Data);
    }

    [Fact]
    public async Task QueryViewModelAlias_OptionsConstructor_Works()
    {
        var client = CreateClient();
        using var vm = new QueryViewModel<string>(
            client,
            new QueryObserverOptions<string>
            {
                QueryKey = ["test"],
                QueryFn = async _ => "from-options",
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal("from-options", vm.Data);
    }

    // ── UseQueryCollection with QueryOptions ────────────────────────

    [Fact]
    public async Task UseQueryCollection_WithQueryOptions_SameItemType_Fetches()
    {
        var client = CreateClient();
        var options = new QueryOptions<IEnumerable<string>>
        {
            QueryKey = ["items"],
            QueryFn = async _ => new[] { "a", "b", "c" },
        };

        using var vm = client.UseQueryCollection(
            options,
            update: (data, items) =>
            {
                items.Clear();
                if (data is not null)
                    foreach (var item in data) items.Add(item);
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(["a", "b", "c"], vm.Items);
    }

    [Fact]
    public async Task UseQueryCollection_WithQueryOptions_DifferentItemType_Fetches()
    {
        var client = CreateClient();
        var options = new QueryOptions<IEnumerable<int>>
        {
            QueryKey = ["numbers"],
            QueryFn = async _ => new[] { 1, 2, 3 },
        };

        using var vm = client.UseQueryCollection<int, string>(
            options,
            update: (data, items) =>
            {
                items.Clear();
                if (data is not null)
                    foreach (var n in data) items.Add($"item-{n}");
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(["item-1", "item-2", "item-3"], vm.Items);
    }

    [Fact]
    public async Task UseQuery_WithQueryOptions_RespectsStaleTime()
    {
        var client = CreateClient();
        var fetchCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["stale-test"],
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return "data";
            },
            StaleTime = TimeSpan.FromMinutes(5),
        };

        using var vm = client.UseQuery(options);
        await WaitForAsync(() => vm.IsSuccess);

        // Data should not be stale because StaleTime is 5 minutes
        Assert.False(vm.IsStale);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task UseQuery_WithQueryOptions_RespectsRetry()
    {
        var client = CreateClient();
        var attemptCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["retry-test"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref attemptCount);
                throw new InvalidOperationException("fail");
            },
            Retry = 1,
        };

        using var vm = client.UseQuery(options);

        // 1 initial + 1 retry = 2 total attempts
        await WaitForAsync(() => attemptCount >= 2);
        await WaitForAsync(() => vm.IsError);
        Assert.Equal(2, attemptCount);
    }

    // ── QueryObserverOptions record with expression ───────────────────

    [Fact]
    public async Task QueryObserverOptions_WithExpression_PreservesBaseOptions()
    {
        var client = CreateClient();
        var baseOptions = new QueryObserverOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "hello",
            StaleTime = TimeSpan.FromSeconds(30),
        };

        var modified = baseOptions with { StaleTime = TimeSpan.FromSeconds(60) };

        // Base unchanged
        Assert.Equal(TimeSpan.FromSeconds(30), baseOptions.StaleTime);
        // Modified has new value
        Assert.Equal(TimeSpan.FromSeconds(60), modified.StaleTime);
        // Other properties preserved
        Assert.Equal(baseOptions.QueryKey, modified.QueryKey);

        using var vm = client.UseQuery(modified);
        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal("hello", vm.Data);
    }

    [Fact]
    public async Task QueryObserverOptions_WithExpression_DifferentGenericArgs_Works()
    {
        var client = CreateClient();
        var baseOptions = new QueryObserverOptions<int, string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "hello world",
            Select = s => s.Length,
            RefetchInterval = TimeSpan.FromSeconds(5),
        };

        // Toggle polling off via `with`
        var pollingOff = baseOptions with { RefetchInterval = TimeSpan.Zero };

        Assert.Equal(TimeSpan.FromSeconds(5), baseOptions.RefetchInterval);
        Assert.Equal(TimeSpan.Zero, pollingOff.RefetchInterval);
        Assert.Same(baseOptions.Select, pollingOff.Select);
        Assert.Same(baseOptions.QueryFn, pollingOff.QueryFn);
    }

    // ── MutationViewModel<TData, TVariables> alias ────────────────────

    [Fact]
    public async Task MutationViewModelAlias_Constructs_AndMutates()
    {
        var client = CreateClient();
        using var vm = new MutationViewModel<string, int>(
            client,
            async (vars, ctx, ct) => $"result-{vars}");

        Assert.True(vm.IsIdle);

        vm.MutateCommand.Execute(42);
        await WaitForAsync(() => vm.IsSuccess);

        Assert.Equal("result-42", vm.Data);
    }

    // ── Simplified UseMutation ────────────────────────────────────────

    [Fact]
    public async Task UseMutation_TwoTypeParams_Works()
    {
        var client = CreateClient();
        using var vm = client.UseMutation<string, int>(
            async (vars, ctx, ct) => $"value-{vars}");

        vm.MutateCommand.Execute(7);
        await WaitForAsync(() => vm.IsSuccess);

        Assert.Equal("value-7", vm.Data);
    }

    [Fact]
    public async Task UseMutation_WithCallbacks_FiresOnSuccess()
    {
        var client = CreateClient();
        var onSuccessCalled = false;
        string? successData = null;

        using var vm = client.UseMutation<string, int>(
            async (vars, ctx, ct) => $"result-{vars}",
            new MutationCallbacks<string, int>
            {
                OnSuccess = (data, vars, ctx) =>
                {
                    onSuccessCalled = true;
                    successData = data;
                    return Task.CompletedTask;
                },
            });

        vm.MutateCommand.Execute(5);
        await WaitForAsync(() => vm.IsSuccess);
        await WaitForAsync(() => onSuccessCalled);

        Assert.Equal("result-5", successData);
    }

    [Fact]
    public async Task UseMutation_WithCallbacks_FiresOnError()
    {
        var client = CreateClient();
        var onErrorCalled = false;
        Exception? capturedError = null;

        using var vm = client.UseMutation<string, int>(
            (vars, ctx, ct) => throw new InvalidOperationException("boom"),
            new MutationCallbacks<string, int>
            {
                OnError = (err, vars, ctx) =>
                {
                    onErrorCalled = true;
                    capturedError = err;
                    return Task.CompletedTask;
                },
            });

        vm.MutateCommand.Execute(1);
        await WaitForAsync(() => vm.IsError);
        await WaitForAsync(() => onErrorCalled);

        Assert.IsType<InvalidOperationException>(capturedError);
        Assert.Equal("boom", capturedError!.Message);
    }

    [Fact]
    public async Task UseMutation_WithCallbacks_FiresOnSettled()
    {
        var client = CreateClient();
        var onSettledCalled = false;

        using var vm = client.UseMutation<string, int>(
            async (vars, ctx, ct) => "done",
            new MutationCallbacks<string, int>
            {
                OnSettled = (data, error, vars, ctx) =>
                {
                    onSettledCalled = true;
                    return Task.CompletedTask;
                },
            });

        vm.MutateCommand.Execute(0);
        await WaitForAsync(() => vm.IsSuccess);
        await WaitForAsync(() => onSettledCalled);

        Assert.True(onSettledCalled);
    }
}
