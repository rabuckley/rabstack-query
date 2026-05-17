using static RabstackQuery.Mvvm.SyncContextTestBase;

namespace RabstackQuery.Mvvm;

public sealed class ThreadMarshallingTests
{
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

    [Fact]
    public async Task QueryViewModel_Should_Marshal_BackgroundNotification_Through_CapturedSyncContext()
    {
        // Arrange — construct the ViewModel with a SyncContext installed,
        // simulating the UI thread.
        var syncContext = new RecordingSynchronizationContext();

        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = WithSyncContext(syncContext, () =>
            new QueryViewModel<string, string>(
                client,
                queryKey: ["qvm-marshal"],
                queryFn: ct =>
                {
                    fetchCount++;
                    return Task.FromResult($"data-{fetchCount}");
                }));

        // Wait for the initial fetch to complete (no SyncContext on this thread,
        // so awaits work without interference from the RecordingSyncContext).
        await WaitForAsync(() => vm.IsSuccess);
        syncContext.Reset();

        // Act — trigger a notification from a thread pool thread.
        // InvalidateQueriesAsync dispatches InvalidateAction synchronously on the
        // calling thread (thread pool). The observer fires OnResultChanged on
        // that same thread pool thread. The observer also triggers a
        // fire-and-forget refetch whose async chain runs on the thread pool.
        await Task.Run(() => client.InvalidateQueriesAsync(["qvm-marshal"]), TestContext.Current.CancellationToken);
        await WaitForAsync(() => vm.Data == $"data-2");

        // Assert — OnResultChanged should have used the CAPTURED SyncContext
        // to Post the property updates.
        Assert.True(syncContext.PostCount > 0,
            "Expected OnResultChanged to Post to the captured SynchronizationContext, " +
            "but Post was never called.");

        vm.Dispose();
    }

    [Fact]
    public async Task MutationViewModel_Should_Marshal_BackgroundNotification_Through_CapturedSyncContext()
    {
        // Arrange — construct with SyncContext, then execute the mutation
        // from a background thread to force notifications onto the thread pool.
        var syncContext = new RecordingSynchronizationContext();

        var client = CreateQueryClient();

        var vm = WithSyncContext(syncContext, () =>
            new MutationViewModel<string, Exception, string, object?>(
                client,
                mutationFn: (input, _, _) => Task.FromResult(input.ToUpper())));

        syncContext.Reset();

        // Act — execute from thread pool. The entire MutateAsync chain runs
        // without a SyncContext, so NotifyListeners and OnResultChanged fire
        // on the thread pool thread.
        await Task.Run(async () => await vm.MutateCommand.ExecuteAsync("hello"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(syncContext.PostCount > 0,
            "Expected MutationViewModel.OnResultChanged to Post to the captured " +
            "SynchronizationContext, but Post was never called.");
        Assert.Equal("HELLO", vm.Data);

        vm.Dispose();
    }

    [Fact]
    public async Task QueryCollectionViewModel_Should_Marshal_BackgroundNotification_Through_CapturedSyncContext()
    {
        var syncContext = new RecordingSynchronizationContext();

        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = WithSyncContext(syncContext, () =>
            new QueryCollectionViewModel<string, string>(
                client,
                queryKey: ["cvm-marshal"],
                queryFn: ct =>
                {
                    fetchCount++;
                    return Task.FromResult<IEnumerable<string>>([$"item-{fetchCount}"]);
                },
                update: static (data, items) =>
                {
                    items.Clear();
                    if (data is null) return;
                    foreach (var item in data) items.Add(item);
                }));

        await WaitForAsync(() => vm.IsSuccess);
        syncContext.Reset();

        // Act
        await Task.Run(() => client.InvalidateQueriesAsync(["cvm-marshal"]), TestContext.Current.CancellationToken);
        await WaitForAsync(() => vm.Items.Count > 0 && vm.Items[0] == "item-2");

        // Assert
        Assert.True(syncContext.PostCount > 0,
            "Expected QueryCollectionViewModel notifications to be marshalled " +
            "through the captured SynchronizationContext.");

        vm.Dispose();
    }

    [Fact]
    public async Task QueryViewModel_PropertyChanged_Should_Be_Delivered_Through_SyncContext_Post()
    {
        // Consumer-perspective test: when a SyncContext is present at
        // construction, all property updates from background notifications
        // should be delivered through SyncContext.Post, ensuring the UI
        // framework can marshal them to the appropriate thread.
        //
        // We can't check actual thread IDs because RecordingSyncContext
        // executes Post callbacks inline on the calling thread (not on the
        // construction thread). In a real MAUI app, Post would dispatch to
        // the UI thread. Here we verify that Post IS called and that
        // PropertyChanged events actually fire — proving the delivery
        // mechanism is correct.
        var syncContext = new RecordingSynchronizationContext();
        var propertyChangedCount = 0;

        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = WithSyncContext(syncContext, () =>
            new QueryViewModel<string, string>(
                client,
                queryKey: ["post-delivery-test"],
                queryFn: ct =>
                {
                    fetchCount++;
                    return Task.FromResult($"data-{fetchCount}");
                }));

        await WaitForAsync(() => vm.IsSuccess);

        vm.PropertyChanged += (_, _) =>
            Interlocked.Increment(ref propertyChangedCount);

        syncContext.Reset();

        // Act — trigger from background thread
        await Task.Run(() => client.InvalidateQueriesAsync(["post-delivery-test"]), TestContext.Current.CancellationToken);
        await WaitForAsync(() => vm.Data == "data-2");

        // Assert — PropertyChanged events fired AND they went through Post.
        Assert.True(propertyChangedCount > 0,
            "Expected PropertyChanged events from the background update.");
        Assert.True(syncContext.PostCount > 0,
            "All property updates should be delivered through SyncContext.Post " +
            "so the UI framework can marshal them to the correct thread.");

        vm.Dispose();
    }

    [Fact]
    public async Task QueryViewModel_Deferred_Post_Should_Not_Update_Properties_After_Dispose()
    {
        // Arrange — use a DeferredSyncContext that queues Post callbacks
        // instead of executing them, so we can control execution timing.
        var syncContext = new DeferredSynchronizationContext();

        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = WithSyncContext(syncContext, () =>
            new QueryViewModel<string, string>(
                client,
                queryKey: ["ghost-qvm"],
                queryFn: ct =>
                {
                    fetchCount++;
                    return Task.FromResult($"data-{fetchCount}");
                }));

        // Execute any initial callbacks so construction state settles
        syncContext.DrainQueue();

        await WaitForAsync(() => vm.IsSuccess || fetchCount >= 1);

        // Drain again to process all pending state
        syncContext.DrainQueue();
        var dataBeforeInvalidation = vm.Data;

        await Task.Run(() => client.InvalidateQueriesAsync(["ghost-qvm"]), TestContext.Current.CancellationToken);

        // Give the fire-and-forget refetch time to complete on the thread pool
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Dispose BEFORE draining the queued callbacks — simulates the
        // race where the user navigates away (disposing the ViewModel)
        // while a notification is still in the dispatcher queue.
        vm.Dispose();

        // Execute ghost callbacks
        var ghostCount = syncContext.DrainQueue();

        if (ghostCount > 0)
        {
            Assert.Equal(dataBeforeInvalidation, vm.Data);
        }
    }

    [Fact]
    public async Task MutationViewModel_Deferred_Post_Should_Not_Update_Properties_After_Dispose()
    {
        var syncContext = new DeferredSynchronizationContext();

        var client = CreateQueryClient();

        var vm = WithSyncContext(syncContext, () =>
            new MutationViewModel<string, Exception, string, object?>(
                client,
                mutationFn: (input, _, _) => Task.FromResult(input.ToUpper())));

        syncContext.DrainQueue();

        // Act — start mutation from background thread, then dispose before
        // the queued Post callback executes.
        var mutateTask = Task.Run(async () =>
            await vm.MutateCommand.ExecuteAsync("hello"), TestContext.Current.CancellationToken);

        // Wait for the mutation to complete on the thread pool
        await mutateTask;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Dispose before draining
        vm.Dispose();

        var ghostCount = syncContext.DrainQueue();

        // Assert — ghost callbacks should not update the disposed ViewModel
        if (ghostCount > 0)
        {
            Assert.True(vm.IsIdle,
                "Ghost callback updated a disposed MutationViewModel. " +
                "Expected properties to remain at pre-Dispose state.");
        }
    }

    [Fact]
    public void QueryViewModel_Constructed_Without_SyncContext_Throws()
    {
        var client = CreateQueryClient();

        var exception = ConstructOnBareThread(() => new QueryViewModel<string, string>(
            client,
            queryKey: ["no-context-query"],
            queryFn: _ => Task.FromResult("data")));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void InfiniteQueryViewModel_Constructed_Without_SyncContext_Throws()
    {
        var client = CreateQueryClient();

        var exception = ConstructOnBareThread(() => new InfiniteQueryViewModel<string, int>(
            client,
            new InfiniteQueryObserverOptions<string, int>
            {
                QueryKey = ["no-context-infinite"],
                QueryFn = ctx => Task.FromResult($"page-{ctx.PageParam}"),
                InitialPageParam = 0,
                GetNextPageParam = static _ => PageParamResult<int>.None,
            }));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void MutationViewModel_Constructed_Without_SyncContext_Throws()
    {
        var client = CreateQueryClient();

        var exception = ConstructOnBareThread(() => new MutationViewModel<string, Exception, string, object?>(
            client,
            mutationFn: (input, _, _) => Task.FromResult(input.ToUpper())));

        Assert.IsType<InvalidOperationException>(exception);
    }

    private static Exception? ConstructOnBareThread(Func<object> construct)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = construct();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.Start();
        thread.Join();
        return captured;
    }
}
