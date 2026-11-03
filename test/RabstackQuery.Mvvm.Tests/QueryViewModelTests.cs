namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for QueryViewModel error handling and state propagation.
/// </summary>
public sealed class QueryViewModelTests : SyncContextTestBase
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public async Task RefetchCommand_Should_Not_Throw_When_Query_Fails()
    {
        // Arrange — a query that always fails; Retry=0 to avoid 7s backoff wait
        var client = CreateQueryClient();
        client.SetQueryDefaults(["refetch-error-test"], new QueryDefaults { Retry = 0 });
        using var vm = new QueryViewModel<string, string>(
            client,
            queryKey: ["refetch-error-test"],
            queryFn: _ => throw new InvalidOperationException("fetch failed"));

        // Wait briefly for the initial fetch (triggered by Enabled = true) to settle
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act — RefetchCommand must not throw; errors surface via IsError/Error
        await vm.RefetchCommand.ExecuteAsync(null);

        // Assert
        Assert.True(vm.IsError);
        Assert.IsType<InvalidOperationException>(vm.Error);
        Assert.Equal("fetch failed", vm.Error.Message);
        Assert.False(vm.IsManualRefreshing);
    }

    [Fact]
    public async Task RefetchCommand_Should_Update_Data_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var callCount = 0;

        using var vm = new QueryViewModel<string, string>(
            client,
            queryKey: ["refetch-success-test"],
            queryFn: _ =>
            {
                callCount++;
                return Task.FromResult($"result-{callCount}");
            });

        // Wait for the initial fetch
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("result-1", vm.Data);

        // Act — refetch should get new data
        await vm.RefetchCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("result-2", vm.Data);
        Assert.True(vm.IsSuccess);
        Assert.False(vm.IsManualRefreshing);
    }

    [Fact]
    public async Task RefetchCommand_Should_Clear_IsManualRefreshing_On_Error()
    {
        // Arrange — Retry=0 to avoid 7s backoff wait
        var client = CreateQueryClient();
        client.SetQueryDefaults(["manual-refresh-error-test"], new QueryDefaults { Retry = 0 });
        using var vm = new QueryViewModel<string, string>(
            client,
            queryKey: ["manual-refresh-error-test"],
            queryFn: _ => throw new InvalidOperationException("fail"));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await vm.RefetchCommand.ExecuteAsync(null);

        // Assert — IsManualRefreshing must be reset even on error (finally block)
        Assert.False(vm.IsManualRefreshing);
    }

    // Use Execute as a binding does; ExecuteAsync alone misses exceptions posted to the UI context.
    [Fact]
    public async Task RefetchCommand_Should_Not_Crash_UI_Context_When_Query_Is_Cancelled()
    {
        var client = CreateQueryClient();
        client.SetQueryData(["refetch-cancelled"], "original");
        var fetchStarted = new TaskCompletionSource();
        var uiContext = new CrashRecordingSynchronizationContext();
        var vm = WithSyncContext(uiContext, () => new QueryViewModel<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["refetch-cancelled"],
                Enabled = false,
                QueryFn = async ctx =>
                {
                    fetchStarted.TrySetResult();
                    await new TaskCompletionSource().Task.WaitAsync(ctx.CancellationToken);
                    return "unreachable";
                },
            }));
        using var _ = vm;

        var execution = WithSyncContext<Task>(uiContext, () =>
        {
            vm.RefetchCommand.Execute(null);
            return vm.RefetchCommand.ExecutionTask ?? Task.CompletedTask;
        });
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.CancelQueriesAsync(["refetch-cancelled"]);
        await Task.WhenAny(execution).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(uiContext.Escaped);
        Assert.Equal(TaskStatus.RanToCompletion, execution.Status);
        Assert.False(vm.IsManualRefreshing);
        Assert.Equal("original", vm.Data);
    }
}
