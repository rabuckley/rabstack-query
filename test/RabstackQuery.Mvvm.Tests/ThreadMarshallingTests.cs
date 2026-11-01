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
                new()
                {
                    MutationFn = (input, _, _) => Task.FromResult(input.ToUpper()),
                }));

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
        var syncContext = new DeferredSynchronizationContext();
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var vm = WithSyncContext(syncContext, () =>
            new QueryViewModel<string, string>(
                client,
                queryKey: ["ghost-qvm"],
                queryFn: _ => fetchGate.Task));
        syncContext.DrainQueue();

        // Fetch continuations use this queue. Stop when the cache updates;
        // draining the queue would also deliver the success notification before disposal.
        fetchGate.SetResult("data");
        while (client.GetQueryData<string>(["ghost-qvm"]) is null)
        {
            await WaitForAsync(() => syncContext.QueuedCount > 0);
            syncContext.RunNext();
        }

        vm.Dispose();
        var changed = RecordPropertyChanges(vm);
        var ghostCount = syncContext.DrainQueue();

        Assert.True(ghostCount > 0);
        Assert.Empty(changed);
        Assert.Null(vm.Data);
        Assert.False(vm.IsSuccess);
    }

    [Fact]
    public async Task MutationViewModel_Deferred_Post_Should_Not_Update_Properties_After_Dispose()
    {
        var syncContext = new DeferredSynchronizationContext();
        var client = CreateQueryClient();

        var vm = WithSyncContext(syncContext, () =>
            new MutationViewModel<string, Exception, string, object?>(
                client,
                new()
                {
                    MutationFn = (input, _, _) => Task.FromResult(input.ToUpper()),
                }));
        syncContext.DrainQueue();

        // Run off-context so mutation completion does not require draining UI notifications.
        await Task.Run(() => vm.MutateCommand.ExecuteAsync("hello"), TestContext.Current.CancellationToken);

        vm.Dispose();
        var changed = RecordPropertyChanges(vm);
        var ghostCount = syncContext.DrainQueue();

        Assert.True(ghostCount > 0);
        Assert.Empty(changed);
        Assert.Null(vm.Data);
        Assert.True(vm.IsIdle);
    }

    [Fact]
    public async Task InfiniteQueryViewModel_Deferred_Post_Should_Not_Update_Properties_After_Dispose()
    {
        var syncContext = new DeferredSynchronizationContext();
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var vm = WithSyncContext(syncContext, () =>
            new InfiniteQueryViewModel<string, int>(
                client,
                new InfiniteQueryObserverOptions<string, int>
                {
                    QueryKey = ["ghost-infinite"],
                    QueryFn = _ => fetchGate.Task,
                    InitialPageParam = 0,
                    GetNextPageParam = static _ => PageParamResult<int>.None,
                }));
        syncContext.DrainQueue();

        // Fetch continuations use this queue. Stop when the cache updates;
        // draining the queue would also deliver the success notification before disposal.
        fetchGate.SetResult("page-0");
        while (client.GetQueryData<InfiniteData<string, int>>(["ghost-infinite"]) is null)
        {
            await WaitForAsync(() => syncContext.QueuedCount > 0);
            syncContext.RunNext();
        }

        vm.Dispose();
        var changed = RecordPropertyChanges(vm);
        var ghostCount = syncContext.DrainQueue();

        Assert.True(ghostCount > 0);
        Assert.Empty(changed);
        Assert.Null(vm.Data);
        Assert.False(vm.IsSuccess);
    }

    [Fact]
    public void QueryViewModel_SubscribeTime_Post_Should_Not_Raise_PropertyChanged_When_Drained()
    {
        var syncContext = new DeferredSynchronizationContext();
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var vm = WithSyncContext(syncContext, () =>
            new QueryViewModel<string, string>(
                client,
                queryKey: ["subscribe-time-qvm"],
                queryFn: _ => fetchGate.Task));

        Assert.True(vm.IsFetching, "Constructor should apply the mount-fetch state synchronously.");
        Assert.True(syncContext.QueuedCount > 0,
            "Precondition: the mount fetch should have posted a subscribe-time notification.");

        var changed = RecordPropertyChanges(vm);
        syncContext.DrainQueue();

        Assert.Empty(changed);
        Assert.True(vm.IsFetching);
        Assert.Equal(QueryStatus.Pending, vm.Status);
    }

    [Fact]
    public void InfiniteQueryViewModel_SubscribeTime_Post_Should_Not_Raise_PropertyChanged_When_Drained()
    {
        var syncContext = new DeferredSynchronizationContext();
        var client = CreateQueryClient();
        var fetchGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var vm = WithSyncContext(syncContext, () =>
            new InfiniteQueryViewModel<string, int>(
                client,
                new InfiniteQueryObserverOptions<string, int>
                {
                    QueryKey = ["subscribe-time-infinite"],
                    QueryFn = _ => fetchGate.Task,
                    InitialPageParam = 0,
                    GetNextPageParam = static _ => PageParamResult<int>.None,
                }));

        Assert.True(vm.IsFetching, "Constructor should apply the mount-fetch state synchronously.");
        Assert.True(syncContext.QueuedCount > 0,
            "Precondition: the mount fetch should have posted a subscribe-time notification.");

        var changed = RecordPropertyChanges(vm);
        syncContext.DrainQueue();

        Assert.Empty(changed);
        Assert.True(vm.IsFetching);
        Assert.Equal(QueryStatus.Pending, vm.Status);
    }

    private static List<string?> RecordPropertyChanges(System.ComponentModel.INotifyPropertyChanged source)
    {
        var changed = new List<string?>();
        source.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
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
            new()
            {
                MutationFn = (input, _, _) => Task.FromResult(input.ToUpper()),
            }));

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
