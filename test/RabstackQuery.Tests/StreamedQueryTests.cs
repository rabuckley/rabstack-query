using System.Runtime.CompilerServices;

namespace RabstackQuery;

/// <summary>
/// Tests for <see cref="StreamedQuery"/>, ported from TanStack's
/// <c>streamedQuery.test.tsx</c> (11 tests).
/// Uses <see cref="SemaphoreSlim"/>-gated streams for deterministic timing
/// instead of <c>Task.Delay</c> with hardcoded timeouts.
/// </summary>
public sealed class StreamedQueryTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    /// <summary>
    /// Helper: test controls when each chunk is emitted via a semaphore gate.
    /// </summary>
    private static async IAsyncEnumerable<int> ControlledStream(
        SemaphoreSlim gate,
        int count,
        int start = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = start; i < start + count; i++)
        {
            await gate.WaitAsync(cancellationToken);
            yield return i;
        }
    }

    /// <summary>
    /// Helper: yields all values immediately (no gating).
    /// </summary>
    private static async IAsyncEnumerable<int> ImmediateStream(
        int count,
        int start = 0)
    {
        for (var i = start; i < start + count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    #region Basic Streaming

    /// <summary>
    /// TanStack: "should stream data from an AsyncIterable"
    /// Each chunk is accumulated into a List and triggers observer notification.
    /// </summary>
    [Fact]
    public async Task Stream_AccumulatesChunksIntoList()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-basic"],
                QueryFn = StreamedQuery.Create(
                    _ => ControlledStream(gate, 3))
            });

        var results = new List<IQueryResult<IReadOnlyList<int>>>();
        var subscription = observer.Subscribe(r => results.Add(r));

        // Act & Assert — initial state
        var current = observer.CurrentResult;
        Assert.Equal(QueryStatus.Pending, current.Status);
        Assert.Equal(FetchStatus.Fetching, current.FetchStatus);
        Assert.Null(current.Data);

        // Release chunk 0
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 1);

        current = observer.CurrentResult;
        Assert.Equal(QueryStatus.Succeeded, current.Status);
        Assert.Equal(FetchStatus.Fetching, current.FetchStatus);
        Assert.Equal([0], current.Data);

        // Release chunk 1
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 2);

        current = observer.CurrentResult;
        Assert.Equal(QueryStatus.Succeeded, current.Status);
        Assert.Equal(FetchStatus.Fetching, current.FetchStatus);
        Assert.Equal([0, 1], current.Data);

        // Release chunk 2 (final)
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        current = observer.CurrentResult;
        Assert.Equal(QueryStatus.Succeeded, current.Status);
        Assert.Equal(FetchStatus.Idle, current.FetchStatus);
        Assert.Equal([0, 1, 2], current.Data);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should allow Arrays to be returned from the stream"
    /// Chunks themselves can be arrays — they get accumulated as list elements.
    /// </summary>
    [Fact]
    public async Task Stream_SupportsArrayChunks()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int[]>, IReadOnlyList<int[]>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int[]>, IReadOnlyList<int[]>>
            {
                QueryKey = ["stream-array-chunks"],
                QueryFn = StreamedQuery.Create<int[]>(
                    _ => YieldArrayChunks(gate, 3))
            });

        var subscription = observer.Subscribe(_ => { });

        // Act — release all chunks
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 1);
        Assert.Equal([[0, 0]], observer.CurrentResult.Data);

        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 2);
        Assert.Equal([0, 0], observer.CurrentResult.Data![0]);
        Assert.Equal([1, 1], observer.CurrentResult.Data![1]);

        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        var data = observer.CurrentResult.Data!;
        Assert.Equal(3, data.Count);
        Assert.Equal([0, 0], data[0]);
        Assert.Equal([1, 1], data[1]);
        Assert.Equal([2, 2], data[2]);

        subscription.Dispose();

        static async IAsyncEnumerable<int[]> YieldArrayChunks(SemaphoreSlim gate, int count)
        {
            for (var i = 0; i < count; i++)
            {
                await gate.WaitAsync();
                yield return [i, i];
            }
        }
    }

    /// <summary>
    /// TanStack: "should handle empty streams"
    /// An empty stream should resolve to an empty list.
    /// </summary>
    [Fact]
    public async Task Stream_HandlesEmptyEnumerable()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-empty"],
                QueryFn = StreamedQuery.Create(
                    _ => EmptyStream())
            });

        // Act
        var subscription = observer.Subscribe(_ => { });

        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        // Assert — empty stream completes immediately, so by the time we check
        // it has already transitioned to Succeeded/Idle.
        var current = observer.CurrentResult;
        Assert.Equal(QueryStatus.Succeeded, current.Status);
        Assert.Equal(FetchStatus.Idle, current.FetchStatus);
        Assert.NotNull(current.Data);
        Assert.Empty(current.Data);

        subscription.Dispose();

#pragma warning disable CS1998 // Async method lacks 'await' operators
        static async IAsyncEnumerable<int> EmptyStream()
#pragma warning restore CS1998
        {
            yield break;
        }
    }

    #endregion

    #region Refetch Modes

    /// <summary>
    /// TanStack: "should replace on refetch" (default reset mode)
    /// On refetch, data is cleared and the query goes back to pending before
    /// streaming fresh data.
    /// </summary>
    [Fact]
    public async Task Refetch_Reset_ClearsDataAndRestreams()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-reset"],
                QueryFn = StreamedQuery.Create(
                    _ => ControlledStream(gate, 2))
            });

        var subscription = observer.Subscribe(_ => { });

        // Complete initial fetch
        gate.Release(2);
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Act — refetch
        _ = observer.RefetchAsync();

        // The reset mode should clear data and go to pending
        await WaitForCondition(() => observer.CurrentResult.Status == QueryStatus.Pending);
        Assert.Equal(QueryStatus.Pending, observer.CurrentResult.Status);
        Assert.Equal(FetchStatus.Fetching, observer.CurrentResult.FetchStatus);
        Assert.Null(observer.CurrentResult.Data);

        // Release first chunk of refetch
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 1);
        Assert.Equal([0], observer.CurrentResult.Data);

        // Release second chunk (final)
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should support refetchMode append"
    /// On refetch, existing data is kept and new chunks are appended.
    /// </summary>
    [Fact]
    public async Task Refetch_Append_KeepsExistingData()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-append"],
                QueryFn = StreamedQuery.Create(
                    _ => ControlledStream(gate, 2),
                    refetchMode: StreamRefetchMode.Append)
            });

        var subscription = observer.Subscribe(_ => { });

        // Complete initial fetch
        gate.Release(2);
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Act — refetch
        _ = observer.RefetchAsync();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Fetching);

        // Data should still be [0, 1] (not cleared)
        Assert.Equal(QueryStatus.Succeeded, observer.CurrentResult.Status);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Release first chunk of refetch — appended
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 3);
        Assert.Equal([0, 1, 0], observer.CurrentResult.Data);

        // Release second chunk (final)
        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);
        Assert.Equal([0, 1, 0, 1], observer.CurrentResult.Data);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should support refetchMode replace"
    /// On refetch, data stays as-is until the stream completes, then gets replaced.
    /// </summary>
    [Fact]
    public async Task Refetch_Replace_BuffersUntilComplete()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);
        var offset = 0;

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-replace"],
                QueryFn = StreamedQuery.Create(
                    _ => ControlledStream(gate, 2, offset),
                    refetchMode: StreamRefetchMode.Replace)
            });

        var subscription = observer.Subscribe(_ => { });

        // Complete initial fetch
        gate.Release(2);
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Act — refetch with different offset
        offset = 100;
        _ = observer.RefetchAsync();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Fetching);

        // Data should still be [0, 1] during refetch
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Release chunks — data should remain [0, 1] while buffering
        gate.Release();
        // Give the stream a moment to process the chunk
        await Task.Delay(50);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        gate.Release();
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        // Now the replace should have happened
        Assert.Equal([100, 101], observer.CurrentResult.Data);

        subscription.Dispose();
    }

    #endregion

    #region Cancellation / Abort

    /// <summary>
    /// TanStack: "should abort ongoing stream when refetch happens"
    /// When signal is consumed and a refetch occurs, the previous stream is
    /// aborted. New stream appends to existing data.
    /// </summary>
    [Fact]
    public async Task Refetch_AbortsStream_WhenSignalConsumed()
    {
        // Arrange — separate gates per invocation so releases can't be consumed
        // by the wrong stream. A TaskCompletionSource replaces the Task.Delay to
        // deterministically signal when the second stream has started.
        var client = CreateQueryClient();
        var gate1 = new SemaphoreSlim(0);
        var gate2 = new SemaphoreSlim(0);
        var secondStreamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-abort-refetch"],
                QueryFn = StreamedQuery.Create(
                    ctx =>
                    {
                        // Consume the signal so cancellation is active, and pass
                        // the token to the stream so it can be interrupted on cancel.
                        var ct = ctx.CancellationToken;
                        var n = Interlocked.Increment(ref invocation);
                        if (n > 1) secondStreamStarted.TrySetResult();
                        return ControlledStream(n == 1 ? gate1 : gate2, 3, cancellationToken: ct);
                    },
                    refetchMode: StreamRefetchMode.Append)
            });

        var subscription = observer.Subscribe(_ => { });

        // Stream first 2 of 3 chunks
        gate1.Release(2);
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 2);
        Assert.Equal([0, 1], observer.CurrentResult.Data);

        // Act — refetch (should abort the first stream at chunk 2 of 3)
        _ = observer.RefetchAsync();

        // Wait for the second stream to actually start
        await secondStreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Release 3 chunks for the new stream. Wait for the expected data count
        // rather than FetchStatus.Idle — the cancelled first fetch dispatches a
        // SuccessState (StreamedQuery catches the cancel and returns partial data)
        // that briefly sets FetchStatus to Idle before the new fetch completes.
        gate2.Release(3);
        await WaitForCondition(() => observer.CurrentResult.Data?.Count == 5);

        // First stream contributed [0, 1], second stream appends [0, 1, 2]
        Assert.Equal([0, 1, 0, 1, 2], observer.CurrentResult.Data);
        Assert.Equal(FetchStatus.Idle, observer.CurrentResult.FetchStatus);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should abort when unsubscribed"
    /// When signal is consumed and all observers unsubscribe, the stream is
    /// aborted and only partial data is cached.
    /// </summary>
    [Fact]
    public async Task Unsubscribe_AbortsStream_WhenSignalConsumed()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-abort-unsub"],
                QueryFn = StreamedQuery.Create(
                    ctx =>
                    {
                        // Consume the signal so cancellation is active, and pass
                        // the token to the stream so it can be interrupted.
                        var ct = ctx.CancellationToken;
                        return ControlledStream(gate, 3, cancellationToken: ct);
                    })
            });

        var subscription = observer.Subscribe(_ => { });

        // Stream first chunk
        gate.Release();
        await WaitForCondition(() =>
        {
            var state = GetQueryState<IReadOnlyList<int>>(client, ["stream-abort-unsub"]);
            return state?.Data?.Count == 1;
        });

        var state = GetQueryState<IReadOnlyList<int>>(client, ["stream-abort-unsub"]);
        Assert.Equal(QueryStatus.Succeeded, state!.Status);
        Assert.Equal(FetchStatus.Fetching, state.FetchStatus);
        Assert.Equal([0], state.Data);

        // Act — unsubscribe (should abort the stream because signal was consumed)
        subscription.Dispose();

        // Wait for the streaming function to finish returning the partial data.
        // Cancel(Revert=true) fires synchronously and briefly sets state to
        // Pending/Idle, but the streaming function catches the cancellation and
        // returns the partial data — the retryer resolves successfully, and
        // FetchCore dispatches the success state.
        await WaitForCondition(() =>
        {
            var s = GetQueryState<IReadOnlyList<int>>(client, ["stream-abort-unsub"]);
            return s is { Status: QueryStatus.Succeeded, FetchStatus: FetchStatus.Idle };
        });

        state = GetQueryState<IReadOnlyList<int>>(client, ["stream-abort-unsub"]);
        Assert.Equal(QueryStatus.Succeeded, state!.Status);
        Assert.Equal(FetchStatus.Idle, state.FetchStatus);
        Assert.Equal([0], state.Data);
    }

    /// <summary>
    /// TanStack: "should not abort when signal not consumed"
    /// When signal is NOT consumed, unsubscribing does not cancel the stream.
    /// The stream continues running in the background.
    /// </summary>
    [Fact]
    public async Task Unsubscribe_DoesNotAbort_WhenSignalNotConsumed()
    {
        // Arrange
        var client = CreateQueryClient();
        var gate = new SemaphoreSlim(0);

        var observer = new QueryObserver<IReadOnlyList<int>, IReadOnlyList<int>>(
            client,
            new QueryObserverOptions<IReadOnlyList<int>, IReadOnlyList<int>>
            {
                QueryKey = ["stream-no-abort"],
                QueryFn = StreamedQuery.Create(
                    // Do NOT consume CancellationToken — signal not consumed
                    _ => ControlledStream(gate, 3))
            });

        var subscription = observer.Subscribe(_ => { });

        // Stream first chunk
        gate.Release();
        await WaitForCondition(() =>
        {
            var state = GetQueryState<IReadOnlyList<int>>(client, ["stream-no-abort"]);
            return state?.Data?.Count == 1;
        });

        var state = GetQueryState<IReadOnlyList<int>>(client, ["stream-no-abort"]);
        Assert.Equal(QueryStatus.Succeeded, state!.Status);
        Assert.Equal(FetchStatus.Fetching, state.FetchStatus);
        Assert.Equal([0], state.Data);

        // Act — unsubscribe (should NOT abort because signal was not consumed)
        subscription.Dispose();

        // Release another chunk — stream should still be running
        gate.Release();
        await WaitForCondition(() =>
        {
            var s = GetQueryState<IReadOnlyList<int>>(client, ["stream-no-abort"]);
            return s?.Data?.Count == 2;
        });

        state = GetQueryState<IReadOnlyList<int>>(client, ["stream-no-abort"]);
        Assert.Equal(FetchStatus.Fetching, state!.FetchStatus);
        Assert.Equal([0, 1], state.Data);

        // Release remaining to allow cleanup
        gate.Release();
    }

    #endregion

    #region Custom Reducer

    /// <summary>
    /// TanStack: "should support custom reducer"
    /// A reducer function accumulates chunks into a custom type.
    /// </summary>
    [Fact]
    public async Task CustomReducer_AccumulatesWithReducer()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<Dictionary<int, bool>, Dictionary<int, bool>>(
            client,
            new QueryObserverOptions<Dictionary<int, bool>, Dictionary<int, bool>>
            {
                QueryKey = ["stream-reducer"],
                QueryFn = StreamedQuery.Create<int, Dictionary<int, bool>>(
                    _ => ImmediateStream(2),
                    (acc, chunk) => new Dictionary<int, bool>(acc) { [chunk] = true },
                    new Dictionary<int, bool>())
            });

        var subscription = observer.Subscribe(_ => { });

        // Act — ImmediateStream may complete before we can check pending, so
        // just wait for completion.
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        // Assert
        var data = observer.CurrentResult.Data;
        Assert.NotNull(data);
        Assert.True(data[0]);
        Assert.True(data[1]);
        Assert.Equal(2, data.Count);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should support custom reducer with initialValue"
    /// The initialValue seeds the reducer so existing entries are preserved.
    /// </summary>
    [Fact]
    public async Task CustomReducer_UsesInitialValue()
    {
        // Arrange
        var client = CreateQueryClient();
        var initialData = new Dictionary<int, bool> { [10] = true, [11] = true };

        var observer = new QueryObserver<Dictionary<int, bool>, Dictionary<int, bool>>(
            client,
            new QueryObserverOptions<Dictionary<int, bool>, Dictionary<int, bool>>
            {
                QueryKey = ["stream-reducer-initial"],
                QueryFn = StreamedQuery.Create<int, Dictionary<int, bool>>(
                    _ => ImmediateStream(2),
                    (acc, chunk) => new Dictionary<int, bool>(acc) { [chunk] = true },
                    initialData)
            });

        var subscription = observer.Subscribe(_ => { });

        // Act
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        // Assert — initial values are preserved, streamed values are added
        var data = observer.CurrentResult.Data;
        Assert.NotNull(data);
        Assert.True(data[10]);
        Assert.True(data[11]);
        Assert.True(data[0]);
        Assert.True(data[1]);
        Assert.Equal(4, data.Count);

        subscription.Dispose();
    }

    /// <summary>
    /// TanStack: "should not call reducer twice when refetchMode is replace"
    /// In replace mode during a refetch, the reducer runs once per chunk (locally),
    /// then the final result is written to the cache. The reducer should not be
    /// invoked again during the cache write.
    /// </summary>
    [Fact]
    public async Task Replace_DoesNotDoubleInvokeReducer()
    {
        // Arrange
        var client = CreateQueryClient();
        var reducerChunks = new List<int>();

        var observer = new QueryObserver<List<int>, List<int>>(
            client,
            new QueryObserverOptions<List<int>, List<int>>
            {
                QueryKey = ["stream-replace-nodupe"],
                QueryFn = StreamedQuery.Create<int, List<int>>(
                    _ => ImmediateStream(3, start: 1),
                    (acc, chunk) =>
                    {
                        reducerChunks.Add(chunk);
                        return [.. acc, chunk];
                    },
                    [],
                    refetchMode: StreamRefetchMode.Replace)
            });

        var subscription = observer.Subscribe(_ => { });

        // Act — wait for initial fetch to complete
        await WaitForCondition(() => observer.CurrentResult.FetchStatus == FetchStatus.Idle);

        Assert.Equal([1, 2, 3], reducerChunks);
        Assert.Equal([1, 2, 3], observer.CurrentResult.Data);

        // Act — refetch
        _ = observer.RefetchAsync();
        await WaitForCondition(() =>
        {
            // Wait for the refetch to complete (6 total reducer calls)
            return reducerChunks.Count == 6;
        });

        // Assert — reducer called exactly once per chunk, per fetch
        Assert.Equal([1, 2, 3, 1, 2, 3], reducerChunks);
        Assert.Equal([1, 2, 3], observer.CurrentResult.Data);

        subscription.Dispose();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Polls a condition with a timeout. Preferred over raw Task.Delay because the
    /// test completes as soon as the condition is met.
    /// </summary>
    private static async Task WaitForCondition(
        Func<bool> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 10)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(
                    $"Condition was not met within {timeoutMs}ms");
            }

            await Task.Delay(pollIntervalMs);
        }
    }

    /// <summary>
    /// Looks up query state from the cache by key. The QueryClient doesn't
    /// expose a public GetQueryState method, so we go through the cache.
    /// </summary>
    private static QueryState<TData>? GetQueryState<TData>(QueryClient client, QueryKey queryKey)
    {
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        return client.QueryCache.Get<TData>(hash)?.State;
    }

    #endregion
}
