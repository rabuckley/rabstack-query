using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery.Mvvm;

/// <summary>
/// TanStack's <c>useMutation</c> has no <c>subscribed</c> option because React's unmount
/// releases the observer. MAUI has no unmount signal, so these tests pin the design decision
/// recorded on <c>MutationViewModel.Subscribed</c>.
/// </summary>
public sealed class MutationViewModelSubscribedTests : SyncContextTestBase
{
    [Fact]
    public async Task Unsubscribing_Should_Not_Cancel_Running_Mutation_And_Resume_Should_Show_Its_Result()
    {
        var client = new QueryClient(new QueryCache());
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken mutationToken = default;
        using var vm = new MutationViewModel<string, string>(client, new MutationOptions<string, string>
        {
            MutationFn = (_, _, ct) =>
            {
                mutationToken = ct;
                return gate.Task;
            },
        });
        var running = vm.InvokeAsync("input", TestContext.Current.CancellationToken);

        vm.Subscribed = false;
        var cancelledByUnsubscribing = mutationToken.IsCancellationRequested;
        gate.SetResult("done");
        var result = await running;
        var dataWhileUnsubscribed = vm.Data;
        vm.Subscribed = true;

        Assert.Equal("done", result);
        Assert.False(cancelledByUnsubscribing);
        Assert.Null(dataWhileUnsubscribed);
        Assert.True(vm.IsSuccess);
        Assert.Equal("done", vm.Data);
    }

    [Fact]
    public async Task Finished_Mutation_Should_Only_Be_Collected_While_Unsubscribed()
    {
        var timeProvider = new FakeTimeProvider();
        var client = new QueryClient(new QueryCache(), timeProvider: timeProvider);
        using var vm = new MutationViewModel<string, string>(client, new MutationOptions<string, string>
        {
            MutationFn = (input, _, _) => Task.FromResult(input),
            GcTime = TimeSpan.FromMinutes(1),
        });
        await vm.InvokeAsync("input", TestContext.Current.CancellationToken);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        var countWhileSubscribed = client.MutationCache.GetAll().Count();
        vm.Subscribed = false;
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, countWhileSubscribed);
        Assert.Empty(client.MutationCache.GetAll());
    }

    [Fact]
    public void Subscribed_True_After_Dispose_Should_Throw()
    {
        var client = new QueryClient(new QueryCache());
        var vm = new MutationViewModel<string, string>(client, new MutationOptions<string, string>
        {
            MutationFn = (input, _, _) => Task.FromResult(input),
        });
        vm.Dispose();

        void Act() => vm.Subscribed = true;

        Assert.Throws<ObjectDisposedException>(Act);
    }
}
