namespace RabstackQuery.Mvvm;

/// <summary>
/// Integration tests covering edge cases around disposal, concurrency, and rapid
/// successive operations on MVVM ViewModels. These tests verify that the ViewModels
/// remain resilient under conditions that are hard to trigger in normal usage but
/// common in real applications (e.g., navigating away mid-fetch, rapid user actions).
/// </summary>
public sealed class IntegrationTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public async Task Disposing_ViewModel_WhileFetchInFlight_DoesNotThrow()
    {
        // Arrange -- a query function that blocks until we explicitly release it,
        // simulating a slow network request that hasn't completed when the user
        // navigates away (disposing the ViewModel).
        var client = CreateQueryClient();
        var fetchStarted = new TaskCompletionSource();
        var fetchGate = new TaskCompletionSource<string>();

        var vm = new QueryViewModel<string, string>(
            client,
            queryKey: ["dispose-inflight"],
            queryFn: async ctx =>
            {
                fetchStarted.TrySetResult();
                return await fetchGate.Task;
            });

        // Wait for the fetch to actually start before disposing.
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act -- dispose while the fetch is still blocked on fetchGate.
        var disposeException = Record.Exception(() => vm.Dispose());

        // Release the blocked fetch after disposal. The observer callback should
        // gracefully handle the disposed subscription (null-checked in OnResultChanged).
        var completionException = await Record.ExceptionAsync(async () =>
        {
            fetchGate.TrySetResult("late-data");
            // Brief yield to let the fire-and-forget continuation run.
            await Task.Delay(100, TestContext.Current.CancellationToken);
        });

        // Assert -- neither disposal nor the late completion should throw.
        Assert.Null(disposeException);
        Assert.Null(completionException);
    }

    [Fact]
    public async Task RapidSuccessiveMutations_AllComplete()
    {
        // Arrange -- track how many mutations have completed via a counter
        // and a TCS that signals when all 5 are done.
        var client = CreateQueryClient();
        var completionCount = 0;
        var allCompleted = new TaskCompletionSource();
        const int totalMutations = 5;

        using var vm = new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (input, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(input.ToUpper());
            },
            options: new()
            {
                OnSuccess = async (data, variables, context, functionContext) =>
                {
                    var count = Interlocked.Increment(ref completionCount);
                    if (count >= totalMutations)
                    {
                        allCompleted.TrySetResult();
                    }
                }
            });

        // Act -- fire 5 mutations in rapid succession without awaiting each one.
        // Each MutateCommand.ExecuteAsync returns a Task, but we deliberately
        // overlap them to stress the mutation pipeline.
        var tasks = new Task[totalMutations];
        for (var i = 0; i < totalMutations; i++)
        {
            tasks[i] = vm.MutateCommand.ExecuteAsync($"input-{i}");
        }

        // Wait for all fire-and-forget completions to propagate through the
        // observer's OnSuccess callback.
        await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Also wait for the command tasks themselves to finish.
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert -- all mutations completed successfully.
        Assert.Equal(totalMutations, Volatile.Read(ref completionCount));
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task ConcurrentObserverSubscription_ThreadSafe()
    {
        // Arrange -- create multiple QueryViewModels that all subscribe to the same
        // query key. This means they all share a single underlying Query instance in
        // the cache, exercising the observer list's thread safety.
        var client = CreateQueryClient();
        const int viewModelCount = 10;
        var viewModels = new QueryViewModel<string, string>[viewModelCount];

        for (var i = 0; i < viewModelCount; i++)
        {
            viewModels[i] = new QueryViewModel<string, string>(
                client,
                queryKey: ["concurrent-key"],
                queryFn: _ => Task.FromResult("shared-data"));
        }

        // Brief yield to let initial fetches settle.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Act -- dispose all ViewModels concurrently from separate thread pool threads.
        // This hammers the observer unsubscribe path from multiple threads simultaneously,
        // which would surface any thread-safety issues in the observer list management.
        var disposeTasks = new Task[viewModelCount];
        for (var i = 0; i < viewModelCount; i++)
        {
            var vm = viewModels[i];
            disposeTasks[i] = Task.Run(() => vm.Dispose(), TestContext.Current.CancellationToken);
        }

        // Assert -- no exception from any concurrent disposal.
        var exception = await Record.ExceptionAsync(
            () => Task.WhenAll(disposeTasks).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }
}
