using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Tests demonstrating thread marshalling bugs in QueryViewModel, MutationViewModel,
/// and QueryCollectionViewModel.
///
/// These tests assert the CORRECT behavior and should FAIL on the current (buggy)
/// implementation, then PASS once the bugs are fixed.
///
/// The core bug: <c>OnResultChanged</c> checks <c>SynchronizationContext.Current</c>
/// on the callback thread (often a thread pool thread where it's null) instead of
/// using a SynchronizationContext captured at construction time. This means property
/// updates are never marshalled to the UI thread — <c>INotifyPropertyChanged</c>
/// fires on arbitrary background threads, which crashes MAUI and WPF.
/// </summary>
public sealed class ThreadMarshallingTests
{
    // ── Test Infrastructure ──────────────────────────────────────────

    /// <summary>
    /// A SynchronizationContext that counts Post calls and executes them
    /// synchronously. Used to verify that ViewModels correctly marshal
    /// property updates through the captured SyncContext.
    /// </summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Reset() => Interlocked.Exchange(ref _postCount, 0);
    }

    /// <summary>
    /// A SynchronizationContext that queues Post callbacks for deferred execution,
    /// simulating a real UI dispatcher queue. Used to test the ghost callback
    /// scenario where a Post is enqueued before Dispose but executes after.
    /// </summary>
    private sealed class DeferredSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int QueuedCount => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) =>
            _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        /// <summary>
        /// Executes all queued callbacks. Returns the number executed.
        /// </summary>
        public int DrainQueue()
        {
            var count = 0;
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
                count++;
            }
            return count;
        }
    }

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

    /// <summary>
    /// Runs an action with a SynchronizationContext installed on the current thread,
    /// restoring the previous context afterwards even if the action throws.
    /// </summary>
    private static T WithSyncContext<T>(SynchronizationContext context, Func<T> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // ── Bug #1: SyncContext.Current checked on wrong thread ──────────
    //
    // OnResultChanged checks SynchronizationContext.Current on the CALLBACK
    // thread instead of using a reference captured at construction. When
    // the callback fires on a thread pool thread (no SyncContext), the
    // check returns null and Post is never called. Property updates go
    // directly to the thread pool thread.

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
        // InvalidateQueries dispatches InvalidateAction synchronously on the
        // calling thread (thread pool). The observer fires OnResultChanged on
        // that same thread pool thread. The observer also triggers a
        // fire-and-forget refetch whose async chain runs on the thread pool.
        await Task.Run(() => client.InvalidateQueries(["qvm-marshal"]), TestContext.Current.CancellationToken);
        await WaitForAsync(() => vm.Data == $"data-2");

        // Assert — OnResultChanged should have used the CAPTURED SyncContext
        // to Post the property updates.
        Assert.True(syncContext.PostCount > 0,
            "Expected OnResultChanged to Post to the captured SynchronizationContext, " +
            "but Post was never called. OnResultChanged checks " +
            "SynchronizationContext.Current on the callback thread (null on " +
            "thread pool) instead of using a context captured at construction.");

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
        await Task.Run(() => client.InvalidateQueries(["cvm-marshal"]), TestContext.Current.CancellationToken);
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
        await Task.Run(() => client.InvalidateQueries(["post-delivery-test"]), TestContext.Current.CancellationToken);
        await WaitForAsync(() => vm.Data == "data-2");

        // Assert — PropertyChanged events fired AND they went through Post.
        Assert.True(propertyChangedCount > 0,
            "Expected PropertyChanged events from the background update.");
        Assert.True(syncContext.PostCount > 0,
            "All property updates should be delivered through SyncContext.Post " +
            "so the UI framework can marshal them to the correct thread.");

        vm.Dispose();
    }

    // ── Bug #2: Ghost callbacks after Dispose ────────────────────────
    //
    // When OnResultChanged Posts to a SyncContext, there's a window where
    // the callback is queued but hasn't executed. If Dispose() runs in
    // that window, the queued callback executes AFTER disposal, setting
    // properties on a dead ViewModel.
    //
    // NOTE: This bug only manifests after bug #1 is fixed (so that Post
    // is actually called). With bug #1 present, Post is never called and
    // the queue stays empty. These tests are written to catch the ghost
    // callback issue once bug #1 is fixed.

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

        // Wait for the initial fetch to complete. Because the DeferredSyncContext
        // queues posts, property updates only happen when we drain. Direct
        // updates (no SyncContext on callback thread) still go through.
        await WaitForAsync(() => vm.IsSuccess || fetchCount >= 1);

        // Drain again to process all pending state
        syncContext.DrainQueue();
        var dataBeforeInvalidation = vm.Data;

        // Act — trigger a notification from a background thread.
        // If bug #1 is fixed, OnResultChanged will Post to DeferredSyncContext,
        // queuing the update callback.
        await Task.Run(() => client.InvalidateQueries(["ghost-qvm"]), TestContext.Current.CancellationToken);

        // Give the fire-and-forget refetch time to complete on the thread pool
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Dispose BEFORE draining the queued callbacks — simulates the
        // race where the user navigates away (disposing the ViewModel)
        // while a notification is still in the dispatcher queue.
        vm.Dispose();

        // Execute ghost callbacks
        var ghostCount = syncContext.DrainQueue();

        // Assert — once bug #1 is fixed, ghost callbacks will be queued.
        // The ViewModel should guard against post-Dispose updates.
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

    // ── Bug #3: Constructor race ─────────────────────────────────────
    //
    // The constructor does:
    //
    //     _subscription = _observer.Subscribe(OnResultChanged);  // fires OnResultChanged synchronously
    //     UpdateFromResult(_observer.GetCurrentResult());         // redundant call, creates race window
    //
    // Subscribe() triggers OnSubscribe() → RefetchAsync() fire-and-forget.
    // The synchronous portion of RefetchAsync dispatches FetchAction, which
    // calls OnResultChanged on the constructor thread. Then the async fetch
    // completes on a thread pool thread and fires OnResultChanged again.
    //
    // If the async completion arrives while the constructor's explicit
    // UpdateFromResult is still running, two threads write to the same
    // ViewModel properties simultaneously — a data race.
    //
    // This race is extremely difficult to trigger deterministically in a
    // test because the window is microseconds wide. The test below verifies
    // the precondition: that async fetch completions DO fire on background
    // threads, confirming the race window exists when combined with the
    // constructor's redundant UpdateFromResult call.

    [Fact]
    public async Task QueryViewModel_AsyncFetch_Fires_PropertyChanged_From_NonConstructionThread()
    {
        // Arrange — use a TaskCompletionSource to control fetch timing.
        // Subscribe to PropertyChanged BEFORE releasing the fetch so
        // we reliably observe events from the background completion.
        var constructionThreadId = Environment.CurrentManagedThreadId;
        var propertyChangedThreadIds = new ConcurrentBag<int>();
        var tcs = new TaskCompletionSource<string>();

        var client = CreateQueryClient();

        // The query function blocks on the TCS. No SyncContext is installed,
        // so _syncContext is null and OnResultChanged will call UpdateFromResult
        // directly on whatever thread the notification arrives on.
        var vm = new QueryViewModel<string, string>(
            client,
            queryKey: ["constructor-race"],
            queryFn: _ => tcs.Task);

        vm.PropertyChanged += (_, _) =>
            propertyChangedThreadIds.Add(Environment.CurrentManagedThreadId);

        // Act — release the fetch from a dedicated thread. Using `new Thread`
        // instead of `Task.Run` guarantees a fresh managed thread ID — the
        // thread pool can reuse the construction thread on low-core CI machines,
        // which would make the background-thread assertion flaky.
        var completionThread = new Thread(() => tcs.SetResult("data"));
        completionThread.Start();
        completionThread.Join();
        await WaitForAsync(() => vm.Data == "data");

        // Assert — PropertyChanged fired from a thread pool thread (not
        // the construction thread). Without a SyncContext, there is nothing
        // preventing background notifications from writing to ViewModel
        // properties concurrently with the constructor thread.
        var backgroundThreadIds = propertyChangedThreadIds
            .Distinct()
            .Where(id => id != constructionThreadId)
            .ToList();

        Assert.NotEmpty(backgroundThreadIds);

        vm.Dispose();
    }
}
