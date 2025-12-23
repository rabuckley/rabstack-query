namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for MutationViewModel error handling and state propagation.
/// </summary>
public sealed class MutationViewModelTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public async Task MutateCommand_Should_Not_Throw_When_Mutation_Fails()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (_, _, _) => throw new InvalidOperationException("Simulated network error"));

        // Act — the command must not throw; errors are surfaced via IsError/Error
        await vm.MutateCommand.ExecuteAsync("input");

        // Assert
        Assert.True(vm.IsError);
        Assert.Equal(MutationStatus.Error, vm.Status);
        Assert.IsType<InvalidOperationException>(vm.Error);
        Assert.Equal("Simulated network error", vm.Error!.Message);
    }

    [Fact]
    public async Task MutateCommand_Should_Propagate_Data_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (input, _, _) => Task.FromResult(input.ToUpper()));

        // Act
        await vm.MutateCommand.ExecuteAsync("hello");

        // Assert
        Assert.True(vm.IsSuccess);
        Assert.Equal(MutationStatus.Success, vm.Status);
        Assert.Equal("HELLO", vm.Data);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task MutateCommand_Should_Invoke_OnError_Callback_Without_Crashing()
    {
        // Arrange
        var client = CreateQueryClient();
        var onErrorCalled = false;

        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (_, _, _) => throw new InvalidOperationException("fail"),
            options: new()
            {
                OnError = async (error, variables, context, functionContext) =>
                {
                    onErrorCalled = true;
                }
            });

        // Act
        await vm.MutateCommand.ExecuteAsync("input");

        // Assert
        Assert.True(onErrorCalled);
        Assert.True(vm.IsError);
    }

    [Fact]
    public async Task MutateCommand_Should_Reset_To_Idle_After_ResetCommand()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (_, _, _) => throw new InvalidOperationException("fail"));

        await vm.MutateCommand.ExecuteAsync("input");
        Assert.True(vm.IsError);

        // Act
        vm.ResetCommand.Execute(null);

        // Assert
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsError);
        Assert.Null(vm.Error);
        Assert.Null(vm.Data);
    }

    [Fact]
    public async Task MutateCommand_Should_Propagate_Cancellation()
    {
        // Arrange — a mutation that blocks until cancelled
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<string>();

        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: async (_, _, ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return await tcs.Task;
            });

        // Act — start the mutation, then cancel it
        var mutateTask = vm.MutateCommand.ExecuteAsync("input");
        vm.MutateCancelCommand.Execute(null);

        // Assert — OperationCanceledException should propagate (CommunityToolkit
        // needs it to transition the command out of its executing state).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutateTask);
    }
}
