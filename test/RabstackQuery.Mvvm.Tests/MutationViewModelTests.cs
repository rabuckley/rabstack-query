namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests for MutationViewModel error handling and state propagation.
/// </summary>
public sealed class MutationViewModelTests : SyncContextTestBase
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
            new()
            {
                MutationFn = (_, _, _) => throw new InvalidOperationException("Simulated network error"),
            });

        // Act — the command must not throw; errors are surfaced via IsError/Error
        await vm.MutateCommand.ExecuteAsync("input");

        // Assert
        Assert.True(vm.IsError);
        Assert.Equal(MutationStatus.Error, vm.Status);
        Assert.IsType<InvalidOperationException>(vm.Error);
        Assert.Equal("Simulated network error", vm.Error.Message);
    }

    [Fact]
    public async Task MutateCommand_Should_Propagate_Data_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            new()
            {
                MutationFn = (input, _, _) => Task.FromResult(input.ToUpper()),
            });

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
            new()
            {
                MutationFn = (_, _, _) => throw new InvalidOperationException("fail"),
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
            new()
            {
                MutationFn = (_, _, _) => throw new InvalidOperationException("fail"),
            });

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

    // No TanStack equivalent: its mutations cannot be cancelled. Closest is mutate(), whose
    // catch(noop) keeps failures off the caller; binding invokes Execute, whose async void wrapper
    // would otherwise rethrow on the UI context.
    [Fact]
    public async Task MutateCancelCommand_Should_Not_Crash_UI_Context()
    {
        // Arrange
        var client = CreateQueryClient();
        var uiContext = new CrashRecordingSynchronizationContext();
        var vm = WithSyncContext(uiContext, () => new MutationViewModel<string, Exception, string, object?>(
            client,
            new()
            {
                MutationFn = async (_, _, ct) =>
                {
                    await new TaskCompletionSource().Task.WaitAsync(ct);
                    return "unreachable";
                },
            }));
        using var _ = vm;

        // Act
        var execution = WithSyncContext<Task>(uiContext, () =>
        {
            vm.MutateCommand.Execute("input");
            return vm.MutateCommand.ExecutionTask ?? Task.CompletedTask;
        });
        vm.MutateCancelCommand.Execute(null);
        await Task.WhenAny(execution).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(uiContext.Escaped);
        Assert.Equal(TaskStatus.RanToCompletion, execution.Status);
        Assert.False(vm.MutateCommand.IsRunning);
    }

    [Fact]
    public async Task InvokeAsync_Should_Throw_When_Callers_Token_Is_Cancelled()
    {
        // Arrange
        var client = CreateQueryClient();
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            new()
            {
                MutationFn = async (_, _, ct) =>
                {
                    await new TaskCompletionSource().Task.WaitAsync(ct);
                    return "unreachable";
                },
            });
        using var cts = new CancellationTokenSource();

        // Act
        var invocation = vm.InvokeAsync("input", cts.Token);
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
    }

    [Fact]
    public async Task MutateCommand_Should_Show_Error_When_MutationFn_Times_Out()
    {
        var client = CreateQueryClient();
        var timeout = new TaskCanceledException("HttpClient.Timeout elapsed");
        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            new() { MutationFn = (_, _, _) => Task.FromException<string>(timeout) });

        await vm.MutateCommand.ExecuteAsync("input");

        Assert.True(vm.IsError);
        Assert.Same(timeout, vm.Error);
        Assert.False(vm.IsPending);
    }

    [Fact]
    public void Constructor_OptionsWithoutMutationFn_ThrowsArgumentException()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?> { Retry = 2 };

        // Act
        void Act() => _ = new MutationViewModel<string, Exception, string, object?>(client, options);

        // Assert
        Assert.Throws<ArgumentException>("options", Act);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        void Act() => _ = new MutationViewModel<string, Exception, string, object?>(client, null!);

        // Assert
        Assert.Throws<ArgumentNullException>("options", Act);
    }
}

