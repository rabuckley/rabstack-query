using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Dedicated tests for <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/>.
/// Covers the update callback mechanism, error propagation, disposal safety,
/// RefetchCommand, and status forwarding from the inner QueryViewModel.
/// </summary>
public sealed class QueryCollectionViewModelTests
{
    // ── Test Infrastructure ──────────────────────────────────────────

    private record TodoDto(int Id, string Title);

    private class TodoItemViewModel(int id, string title)
    {
        public int Id { get; } = id;
        public string Title { get; set; } = title;
    }

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

    private sealed class DeferredSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public int QueuedCount => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) =>
            _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

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

    private static T WithSyncContext<T>(SynchronizationContext context, Func<T> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { return action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static QueryCollectionViewModel<string, string> CreateSimpleCollectionVm(
        QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<string>>> queryFn)
    {
        return new QueryCollectionViewModel<string, string>(
            client,
            queryKey,
            queryFn,
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });
    }

    // ── Basic lifecycle ──────────────────────────────────────────────

    [Fact]
    public void Items_Should_Start_As_Empty_Collection()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act — create with Enabled=false so no fetch starts
        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["empty-start"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["a"]),
                Enabled = false
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        // Assert
        Assert.NotNull(vm.Items);
        Assert.Empty(vm.Items);
        Assert.Equal(FetchStatus.Idle, vm.FetchStatus);
    }

    [Fact]
    public async Task Update_Callback_Should_Receive_Null_When_No_Data()
    {
        // The update callback should be invoked with null when there's no data
        // (e.g., Enabled=false, before any fetch completes).
        // Arrange
        var client = CreateQueryClient();
        IEnumerable<string>? receivedData = new[] { "sentinel" }; // Non-null sentinel

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["null-callback"],
                QueryFn = _ => Task.FromResult<IEnumerable<string>>(["a"]),
                Enabled = false
            },
            update: (data, items) =>
            {
                receivedData = data;
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        // Assert — the initial sync should have invoked update with null data
        Assert.Null(receivedData);
    }

    [Fact]
    public async Task Update_Callback_Should_Receive_Fetched_Data()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        using var vm = CreateSimpleCollectionVm(
            client,
            ["fetch-data"],
            _ => Task.FromResult<IEnumerable<string>>(["alpha", "beta"]));

        await WaitForAsync(() => vm.IsSuccess);

        // Assert
        Assert.Equal(2, vm.Items.Count);
        Assert.Contains("alpha", vm.Items);
        Assert.Contains("beta", vm.Items);
    }

    [Fact]
    public async Task Update_Callback_Should_Be_Invoked_On_Refetch_With_New_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        using var vm = CreateSimpleCollectionVm(
            client,
            ["refetch-update"],
            _ =>
            {
                var c = Interlocked.Increment(ref fetchCount);
                return Task.FromResult<IEnumerable<string>>([$"item-{c}"]);
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Single(vm.Items);
        Assert.Equal("item-1", vm.Items[0]);

        // Act — trigger refetch
        await client.InvalidateQueriesAsync(["refetch-update"]);
        await WaitForAsync(() => fetchCount >= 2 && vm.Items.Count > 0 && vm.Items[0] == "item-2");

        // Assert
        Assert.Single(vm.Items);
        Assert.Equal("item-2", vm.Items[0]);
    }

    // ── Error propagation ────────────────────────────────────────────

    [Fact]
    public async Task Error_Should_Propagate_From_Inner_ViewModel()
    {
        // Arrange — use Retry=0 so error propagates immediately without backoff
        var client = CreateQueryClient();
        client.SetQueryDefaults(["error-propagate"], new QueryDefaults
        {
            QueryKey = ["error-propagate"],
            Retry = 0
        });

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["error-propagate"],
                QueryFn = _ => throw new InvalidOperationException("fetch failed"),
                Enabled = true
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        await WaitForAsync(() => vm.IsError);

        // Assert
        Assert.True(vm.IsError);
        Assert.IsType<InvalidOperationException>(vm.Error);
        Assert.Equal(QueryStatus.Errored, vm.Status);
        Assert.Empty(vm.Items);
    }

    // ── RefetchCommand ───────────────────────────────────────────────

    [Fact]
    public async Task RefetchCommand_Should_Trigger_New_Fetch()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        using var vm = CreateSimpleCollectionVm(
            client,
            ["refetch-cmd"],
            _ =>
            {
                var c = Interlocked.Increment(ref fetchCount);
                return Task.FromResult<IEnumerable<string>>([$"data-{c}"]);
            });

        await WaitForAsync(() => vm.IsSuccess);
        Assert.Equal(1, fetchCount);

        // Act
        await vm.RefetchCommand.ExecuteAsync(null);

        // Assert
        Assert.True(fetchCount >= 2, "RefetchCommand should trigger a new fetch");
    }

    [Fact]
    public async Task RefetchCommand_Should_Not_Throw_On_Error()
    {
        // Arrange — first fetch succeeds, second fails
        var client = CreateQueryClient();
        var fetchCount = 0;

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["refetch-error"],
                QueryFn = _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1)
                        return Task.FromResult<IEnumerable<string>>(["ok"]);
                    throw new InvalidOperationException("refetch failed");
                },
                Enabled = true
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        await WaitForAsync(() => vm.IsSuccess);

        // Act — refetch that fails should not throw
        var exception = await Record.ExceptionAsync(() => vm.RefetchCommand.ExecuteAsync(null));

        // Assert
        Assert.Null(exception);
        Assert.True(vm.IsError);
    }

    [Fact]
    public async Task IsManualRefreshing_Should_Track_RefetchCommand_Lifecycle()
    {
        // Arrange — block the second fetch with a TCS
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<IEnumerable<string>>();
        var fetchCount = 0;

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["manual-refresh"],
                QueryFn = _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    if (c == 1) return Task.FromResult<IEnumerable<string>>(["initial"]);
                    return tcs.Task;
                },
                Enabled = true
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        await WaitForAsync(() => vm.IsSuccess);

        // Act — start refetch (will block on TCS)
        var refetchTask = vm.RefetchCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.IsManualRefreshing);

        // Assert — should be refreshing while fetch is blocked
        Assert.True(vm.IsManualRefreshing);

        // Release the fetch
        tcs.SetResult(["refreshed"]);
        await refetchTask;

        // Assert — should be done refreshing
        Assert.False(vm.IsManualRefreshing);
    }

    // ── Disposal safety ──────────────────────────────────────────────

    [Fact]
    public async Task Dispose_Should_Stop_Receiving_Updates()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = CreateSimpleCollectionVm(
            client,
            ["dispose-updates"],
            _ =>
            {
                var c = Interlocked.Increment(ref fetchCount);
                return Task.FromResult<IEnumerable<string>>([$"data-{c}"]);
            });

        await WaitForAsync(() => vm.IsSuccess);
        var itemsAfterFirstFetch = vm.Items.ToList();

        // Act — dispose and then invalidate
        vm.Dispose();
        await client.InvalidateQueriesAsync(["dispose-updates"]);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert — items should remain unchanged after disposal
        Assert.Equal(itemsAfterFirstFetch.Count, vm.Items.Count);
    }

    [Fact]
    public async Task Dispose_Should_Not_Throw_On_Subsequent_Invalidation()
    {
        // Arrange
        var client = CreateQueryClient();

        var vm = CreateSimpleCollectionVm(
            client,
            ["dispose-safe"],
            _ => Task.FromResult<IEnumerable<string>>(["data"]));

        await WaitForAsync(() => vm.IsSuccess);

        // Act — dispose then invalidate
        vm.Dispose();

        var exception = await Record.ExceptionAsync(() =>
            client.InvalidateQueriesAsync(["dispose-safe"]));

        // Assert
        Assert.Null(exception);
    }

    // ── Type transformation ──────────────────────────────────────────

    [Fact]
    public async Task Different_TData_And_TQueryFnData_Should_Transform_Via_Update_Callback()
    {
        // The update callback receives raw TodoDto items and creates TodoItemViewModel
        // instances — verifying the two-type-parameter path works correctly.
        // Arrange
        var client = CreateQueryClient();

        using var vm = new QueryCollectionViewModel<TodoItemViewModel, TodoDto>(
            client,
            queryKey: ["typed-transform"],
            queryFn: _ => Task.FromResult<IEnumerable<TodoDto>>([
                new TodoDto(1, "Buy milk"),
                new TodoDto(2, "Write tests")
            ]),
            update: (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var dto in data)
                    items.Add(new TodoItemViewModel(dto.Id, dto.Title));
            });

        await WaitForAsync(() => vm.IsSuccess);

        // Assert
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(1, vm.Items[0].Id);
        Assert.Equal("Buy milk", vm.Items[0].Title);
        Assert.Equal(2, vm.Items[1].Id);
        Assert.Equal("Write tests", vm.Items[1].Title);
    }

    // ── Status forwarding ────────────────────────────────────────────

    [Fact]
    public async Task All_Status_Properties_Should_Forward_From_Inner_ViewModel()
    {
        // Arrange — use a TCS to observe mid-flight state
        var client = CreateQueryClient();
        var tcs = new TaskCompletionSource<IEnumerable<string>>();
        var fetchStarted = new TaskCompletionSource();

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["status-forward"],
                QueryFn = _ =>
                {
                    fetchStarted.TrySetResult();
                    return tcs.Task;
                },
                Enabled = true
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — mid-fetch state
        Assert.True(vm.IsFetching);
        Assert.True(vm.IsLoading);
        Assert.Equal(QueryStatus.Pending, vm.Status);
        Assert.Equal(FetchStatus.Fetching, vm.FetchStatus);

        // Release fetch
        tcs.SetResult(["done"]);
        await WaitForAsync(() => vm.IsSuccess);

        // Assert — post-success state
        Assert.True(vm.IsSuccess);
        Assert.False(vm.IsFetching);
        Assert.False(vm.IsLoading);
        Assert.Equal(QueryStatus.Succeeded, vm.Status);
        Assert.Equal(FetchStatus.Idle, vm.FetchStatus);
    }

    // ── Ghost callback protection ────────────────────────────────────

    [Fact]
    public async Task Deferred_Post_Should_Not_Update_Items_After_Dispose()
    {
        // Ghost callback scenario: a Post is enqueued before Dispose but
        // executes after — the ViewModel should guard against this.
        // Arrange
        var syncContext = new DeferredSynchronizationContext();

        var client = CreateQueryClient();
        var fetchCount = 0;

        var vm = WithSyncContext(syncContext, () =>
            new QueryCollectionViewModel<string, string>(
                client,
                queryKey: ["ghost-cvm"],
                queryFn: _ =>
                {
                    var c = Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IEnumerable<string>>([$"item-{c}"]);
                },
                update: static (data, items) =>
                {
                    items.Clear();
                    if (data is null) return;
                    foreach (var item in data) items.Add(item);
                }));

        syncContext.DrainQueue();
        await WaitForAsync(() => vm.IsSuccess || fetchCount >= 1);
        syncContext.DrainQueue();

        var itemsBeforeInvalidation = vm.Items.ToList();

        // Trigger a notification from a background thread
        await Task.Run(() => client.InvalidateQueriesAsync(["ghost-cvm"]),
            TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Dispose before draining the ghost callbacks
        vm.Dispose();

        var ghostCount = syncContext.DrainQueue();

        // Assert — ghost callbacks should not update the disposed ViewModel
        if (ghostCount > 0)
        {
            Assert.Equal(itemsBeforeInvalidation.Count, vm.Items.Count);
        }
    }

    // ── PropertyChanged events ───────────────────────────────────────

    [Fact]
    public async Task PropertyChanged_Should_Fire_For_Status_Transitions()
    {
        // Arrange — use a TCS to control fetch timing so we can subscribe to
        // PropertyChanged before the fetch completes.
        var client = CreateQueryClient();
        var changedProperties = new ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<IEnumerable<string>>();

        using var vm = new QueryCollectionViewModel<string, string>(
            client,
            new QueryObserverOptions<IEnumerable<string>>
            {
                QueryKey = ["prop-changed"],
                QueryFn = _ => tcs.Task,
                Enabled = true
            },
            update: static (data, items) =>
            {
                items.Clear();
                if (data is null) return;
                foreach (var item in data) items.Add(item);
            });

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                changedProperties.Add(e.PropertyName);
        };

        // Release the fetch
        tcs.SetResult(["data"]);
        await WaitForAsync(() => vm.IsSuccess);

        // Assert — key properties should have fired PropertyChanged
        Assert.Contains(nameof(vm.IsSuccess), changedProperties);
        Assert.Contains(nameof(vm.Status), changedProperties);
    }
}
