namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for QueryViewModel error handling and state propagation.
/// </summary>
public sealed class QueryViewModelTests
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
        client.SetQueryDefaults(["refetch-error-test"], new QueryDefaults { QueryKey = ["refetch-error-test"], Retry = 0 });
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
        Assert.Equal("fetch failed", vm.Error!.Message);
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
        client.SetQueryDefaults(["manual-refresh-error-test"], new QueryDefaults { QueryKey = ["manual-refresh-error-test"], Retry = 0 });
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
}
