namespace RabstackQuery.Tests;

/// <summary>
/// Integration tests for <see cref="NetworkMode"/> enforcement via
/// <see cref="Query{TData}"/>. Tests verify that fetch execution is gated
/// on network state, that paused queries resume on connectivity changes,
/// and that the <see cref="NetworkModeHelper.CanFetch"/> utility works
/// correctly.
/// </summary>
public sealed class NetworkModeTests : IDisposable
{
    // Each test gets its own managers to avoid cross-test interference
    // through the global OnlineManager.Instance / FocusManager.Instance singletons.
    private readonly OnlineManager _onlineManager = new();
    private readonly FocusManager _focusManager = new();
    private readonly QueryClient _client;

    public NetworkModeTests()
    {
        _client = new QueryClient(
            new QueryCache(),
            onlineManager: _onlineManager,
            focusManager: _focusManager);
    }

    public void Dispose() => _client.Dispose();

    private Query<TData> BuildQuery<TData>(
        QueryKey queryKey,
        NetworkMode networkMode = NetworkMode.Online,
        int retry = 0,
        Func<int, Exception, TimeSpan>? retryDelay = null)
    {
        var cache = _client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        var options = new QueryConfiguration<TData>
        {
            QueryKey = queryKey,
            QueryHash = queryHash,
            GcTime = TimeSpan.FromMinutes(5),
            Retry = retry,
            RetryDelay = retryDelay,
            NetworkMode = networkMode
        };
        return cache.Build<TData, TData>(_client, options);
    }

    /// <summary>
    /// Subscribes to the <see cref="QueryCache"/> and signals the given
    /// <see cref="TaskCompletionSource"/> when a <see cref="PauseAction"/>
    /// dispatch is observed. This avoids <c>Task.Delay</c> for tests that
    /// need to wait for a query to enter the paused state.
    /// </summary>
    private IDisposable SubscribePauseListener(TaskCompletionSource paused)
    {
        var cache = _client.GetQueryCache();
        return cache.Subscribe(@event =>
        {
            if (@event is QueryCacheQueryUpdatedEvent { Action: PauseAction })
            {
                paused.TrySetResult();
            }
        });
    }

    #region CanFetch utility

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanFetch_Online_RequiresIsOnline(bool isOnline)
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(isOnline);

        // Act & Assert
        Assert.Equal(isOnline, NetworkModeHelper.CanFetch(NetworkMode.Online, onlineManager));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanFetch_Always_IgnoresIsOnline(bool isOnline)
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(isOnline);

        // Act & Assert
        Assert.True(NetworkModeHelper.CanFetch(NetworkMode.Always, onlineManager));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanFetch_OfflineFirst_IgnoresIsOnline(bool isOnline)
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(isOnline);

        // Act & Assert
        Assert.True(NetworkModeHelper.CanFetch(NetworkMode.OfflineFirst, onlineManager));
    }

    #endregion

    #region FetchStatus at fetch start

    [Fact(Timeout = 10_000)]
    public async Task Fetch_StartsAsPaused_WhenOfflineAndNetworkModeOnline()
    {
        // Arrange
        _onlineManager.SetOnline(false);
        var query = BuildQuery<string>(["offline-test"]);

        query.SetQueryFn(_ => Task.FromResult("data"));

        // Act — kick off a fetch while offline
        var fetchTask = query.Fetch();

        // The FetchAction reducer should set FetchStatus.Paused immediately
        // because CanFetch returns false when offline.
        Assert.Equal(FetchStatus.Paused, query.State!.FetchStatus);

        // Clean up — go online so the fetch can complete.
        // SetOnline(true) fires OnlineChanged → QueryClient → query.OnOnline()
        // → _retryer?.Continue() — wakes the paused retryer.
        _onlineManager.SetOnline(true);

        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("data", query.State.Data);
    }

    [Fact(Timeout = 10_000)]
    public async Task Fetch_StartsAsFetching_WhenOfflineAndNetworkModeAlways()
    {
        // Arrange
        _onlineManager.SetOnline(false);
        var query = BuildQuery<string>(["always-test"],
            networkMode: NetworkMode.Always);

        query.SetQueryFn(_ => Task.FromResult("data"));

        // Act
        await query.Fetch();

        // Assert — should have started as Fetching (not Paused) since Always mode
        // ignores network state. By assertion time the fetch completed to Idle.
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
        Assert.Equal("data", query.State.Data);
    }

    #endregion

    #region Resume on online

    [Fact(Timeout = 10_000)]
    public async Task Fetch_ResumesOnOnline_WhenPaused()
    {
        // Arrange
        _onlineManager.SetOnline(false);
        var query = BuildQuery<string>(["resume-test"]);

        var fetchCompleted = new TaskCompletionSource<string>();

        query.SetQueryFn(_ =>
        {
            fetchCompleted.TrySetResult("fetched");
            return Task.FromResult("fetched");
        });

        // Subscribe to cache events to detect when the query enters pause
        var paused = new TaskCompletionSource();
        using var subscription = SubscribePauseListener(paused);

        // Act — start fetch while offline (will pause)
        var fetchTask = query.Fetch();

        // Wait for the PauseAction dispatch deterministically
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FetchStatus.Paused, query.State!.FetchStatus);

        // Go online — fires OnlineChanged → QueryClient → query.OnOnline() →
        // _retryer?.Continue()
        _onlineManager.SetOnline(true);

        // Wait for the fetch to complete
        var result = await fetchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("fetched", result);
        Assert.Equal(QueryStatus.Succeeded, query.State.Status);
        Assert.Equal(FetchStatus.Idle, query.State.FetchStatus);
    }

    #endregion

    #region OnFocus resumes paused retryer

    [Fact(Timeout = 10_000)]
    public async Task OnFocus_ResumesPausedRetryer_WhenOnline()
    {
        // Arrange — start offline so the fetch pauses at the beginning.
        // Verify that OnFocus (with connectivity restored) wakes it.
        _onlineManager.SetOnline(false);

        var query = BuildQuery<string>(["focus-resume-test"]);

        query.SetQueryFn(_ => Task.FromResult("ok"));

        // Subscribe to cache events to detect when the query enters pause
        var paused = new TaskCompletionSource();
        using var subscription = SubscribePauseListener(paused);

        var fetchTask = query.Fetch();

        // Wait for the PauseAction dispatch deterministically
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FetchStatus.Paused, query.State!.FetchStatus);

        // Act — go online and simulate focus event.
        // SetOnline(true) won't call query.OnFocus(), only query.OnOnline().
        // But OnOnline also calls _retryer?.Continue(). Either event can wake
        // the retryer. We explicitly call OnFocus to test that code path.
        _onlineManager.SetOnline(true);
        _focusManager.SetFocused(true); // no-op if already true, but explicit
        query.OnFocus();

        await fetchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(FetchStatus.Idle, query.State!.FetchStatus);
        Assert.Equal("ok", query.State.Data);
    }

    #endregion

    #region Observer-level NetworkMode override

    [Fact(Timeout = 10_000)]
    public async Task Observer_NetworkModeAlways_FetchesWhileOffline()
    {
        // Arrange — offline, but observer overrides to Always so fetch proceeds.
        _onlineManager.SetOnline(false);

        var observer = new QueryObserver<string, string>(
            _client,
            new QueryObserverOptions<string>
            {
                QueryKey = ["observer-always-offline"],
                QueryFn = _ => Task.FromResult("always-data"),
                NetworkMode = NetworkMode.Always
            });

        // Act — subscribe to trigger the initial fetch
        var resultTcs = new TaskCompletionSource<IQueryResult<string>>();
        using var unsubscribe = observer.Subscribe(result =>
        {
            if (result.IsSuccess)
                resultTcs.TrySetResult(result);
        });

        var result = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — fetch completed despite being offline
        Assert.Equal("always-data", result.Data);
        Assert.False(result.IsPaused);
    }

    [Fact(Timeout = 10_000)]
    public async Task Observer_NetworkModeOnline_PausesWhileOffline()
    {
        // Arrange — offline with explicit NetworkMode.Online on observer
        _onlineManager.SetOnline(false);

        var observer = new QueryObserver<string, string>(
            _client,
            new QueryObserverOptions<string>
            {
                QueryKey = ["observer-online-offline"],
                QueryFn = _ => Task.FromResult("data"),
                NetworkMode = NetworkMode.Online
            });

        // Act — subscribe to trigger the initial fetch
        var pausedTcs = new TaskCompletionSource<IQueryResult<string>>();
        using var unsubscribe = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult(result);
        });

        var result = await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — query paused because we're offline
        Assert.True(result.IsPaused);
        Assert.Equal(FetchStatus.Paused, result.FetchStatus);
    }

    [Fact(Timeout = 10_000)]
    public async Task Observer_NullNetworkMode_DefaultsToOnline()
    {
        // Arrange — offline, observer leaves NetworkMode as null (should default to Online)
        _onlineManager.SetOnline(false);

        var observer = new QueryObserver<string, string>(
            _client,
            new QueryObserverOptions<string>
            {
                QueryKey = ["observer-default-offline"],
                QueryFn = _ => Task.FromResult("data"),
                // NetworkMode intentionally not set — null means use default (Online)
            });

        // Act
        var pausedTcs = new TaskCompletionSource<IQueryResult<string>>();
        using var unsubscribe = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult(result);
        });

        var result = await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — paused, proving null defaults to Online behavior
        Assert.True(result.IsPaused);
    }

    #endregion

    #region Mutation NetworkMode

    [Fact(Timeout = 10_000)]
    public async Task Mutation_NetworkModeOnline_PausesWhenOffline()
    {
        // Arrange — offline with NetworkMode.Online
        _onlineManager.SetOnline(false);

        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) => Task.FromResult(input.ToUpper()),
                NetworkMode = NetworkMode.Online
            });

        // Act — start mutation while offline
        var resultTcs = new TaskCompletionSource<IMutationResult<string, Exception>>();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                resultTcs.TrySetResult(result);
        });

        var mutateTask = observer.MutateAsync("hello");

        var pausedResult = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — mutation is paused and pending
        Assert.True(pausedResult.IsPaused);
        Assert.True(pausedResult.IsPending);

        // Clean up — go online so mutation completes
        _onlineManager.SetOnline(true);
        var data = await mutateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("HELLO", data);
    }

    [Fact(Timeout = 10_000)]
    public async Task Mutation_NetworkModeAlways_ExecutesWhenOffline()
    {
        // Arrange — offline with NetworkMode.Always
        _onlineManager.SetOnline(false);

        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) => Task.FromResult(input.ToUpper()),
                NetworkMode = NetworkMode.Always
            });

        // Act — mutation should complete despite being offline
        var data = await observer.MutateAsync("hello")
            .WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("HELLO", data);
        var result = observer.GetCurrentResult();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsPaused);
    }

    [Fact(Timeout = 10_000)]
    public async Task Mutation_NetworkModeOnline_ResumesOnOnline()
    {
        // Arrange — offline, mutation will pause
        _onlineManager.SetOnline(false);

        var mutationExecuted = new TaskCompletionSource<string>();
        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = async (input, ctx, ct) =>
                {
                    var result = input.ToUpper();
                    mutationExecuted.TrySetResult(result);
                    return result;
                },
                NetworkMode = NetworkMode.Online
            });

        // Start mutation while offline — will pause
        var pausedTcs = new TaskCompletionSource<IMutationResult<string, Exception>>();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult(result);
        });

        var mutateTask = observer.MutateAsync("hello");

        await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observer.GetCurrentResult().IsPaused);

        // Act — go online to resume
        _onlineManager.SetOnline(true);

        var data = await mutationExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await mutateTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — mutation completed
        Assert.Equal("HELLO", data);
        var finalResult = observer.GetCurrentResult();
        Assert.True(finalResult.IsSuccess);
        Assert.False(finalResult.IsPaused);
    }

    [Fact(Timeout = 10_000)]
    public async Task ResumePausedMutations_ResumesAll()
    {
        // Arrange — two mutations paused offline
        _onlineManager.SetOnline(false);

        var results = new List<string>();
        var pauseCount = 0;
        var allPaused = new TaskCompletionSource();

        var subscriptions = new List<IDisposable>();
        var makeObserver = (string name) =>
        {
            var obs = new MutationObserver<string, Exception, string, object?>(
                _client,
                new MutationOptions<string, Exception, string, object?>
                {
                    MutationFn = async (input, ctx, ct) =>
                    {
                        var result = $"{name}:{input}";
                        lock (results) results.Add(result);
                        return result;
                    },
                    NetworkMode = NetworkMode.Online
                });
            subscriptions.Add(obs.Subscribe(result =>
            {
                if (result.IsPaused && Interlocked.Increment(ref pauseCount) == 2)
                    allPaused.TrySetResult();
            }));
            return obs;
        };

        var obs1 = makeObserver("m1");
        var obs2 = makeObserver("m2");

        try
        {
            var task1 = obs1.MutateAsync("a");
            var task2 = obs2.MutateAsync("b");

            // Wait for both to be paused
            await allPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Act — go online, which triggers ResumePausedMutations via OnOnlineChanged
            _onlineManager.SetOnline(true);

            await Task.WhenAll(
                task1.WaitAsync(TimeSpan.FromSeconds(5)),
                task2.WaitAsync(TimeSpan.FromSeconds(5)));

            // Assert — both completed
            Assert.Equal(2, results.Count);
            Assert.Contains("m1:a", results);
            Assert.Contains("m2:b", results);
        }
        finally
        {
            foreach (var sub in subscriptions) sub.Dispose();
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task ResumePausedMutations_NoopWhenOffline()
    {
        // Arrange — a paused mutation
        _onlineManager.SetOnline(false);

        var executed = false;
        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) =>
                {
                    executed = true;
                    return Task.FromResult("done");
                },
                NetworkMode = NetworkMode.Online
            });

        var pausedTcs = new TaskCompletionSource();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult();
        });

        var mutateTask = observer.MutateAsync("test");
        await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — call ResumePausedMutations while still offline
        _client.ResumePausedMutations();

        // Assert — mutation is still paused because we're offline.
        // This is a negative assertion: we're verifying the mutation did NOT execute.
        // Task.Delay is the only option here — there's no event to signal "nothing happened".
        // Per CLAUDE.md: "If Task.Delay is the only option, keep it generous and document why."
        await Task.Delay(250);
        Assert.False(executed);
        Assert.True(observer.GetCurrentResult().IsPaused);

        // Clean up — go online to let mutation complete
        _onlineManager.SetOnline(true);
        await mutateTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 10_000)]
    public async Task IMutationResult_ExposesIsPaused()
    {
        // Arrange
        _onlineManager.SetOnline(false);

        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) => Task.FromResult("result"),
                NetworkMode = NetworkMode.Online
            });

        var pausedTcs = new TaskCompletionSource<IMutationResult<string, Exception>>();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult(result);
        });

        // Act
        var mutateTask = observer.MutateAsync("test");
        var result = await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — IsPaused is surfaced through IMutationResult
        Assert.True(result.IsPaused);
        Assert.True(result.IsPending);

        // Also verify via GetCurrentResult
        var current = observer.GetCurrentResult();
        Assert.True(current.IsPaused);

        // Clean up
        _onlineManager.SetOnline(true);
        await mutateTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 10_000)]
    public async Task Mutation_Continue_NoopAfterCompletion()
    {
        // Arrange — start offline so mutation pauses, then go online to complete
        _onlineManager.SetOnline(false);

        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) => Task.FromResult("done"),
                NetworkMode = NetworkMode.Online
            });

        var pausedTcs = new TaskCompletionSource();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused)
                pausedTcs.TrySetResult();
        });

        var mutateTask = observer.MutateAsync("test");
        await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Go online to let it complete
        _onlineManager.SetOnline(true);
        await mutateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(observer.GetCurrentResult().IsSuccess);

        // Act — fire another online event after completion. This calls
        // Continue() on a mutation whose retryer is already disposed/nulled.
        // Should be a safe no-op.
        _onlineManager.SetOnline(false);
        _onlineManager.SetOnline(true);

        // Assert — still success, no errors thrown
        Assert.True(observer.GetCurrentResult().IsSuccess);
    }

    [Fact(Timeout = 10_000)]
    public async Task Mutation_ScopeAndNetworkPauseCompose()
    {
        // Arrange — start online with two scoped mutations. The first holds the
        // scope gate via a TCS. Before releasing the scope gate, go offline so
        // the second mutation transitions from scope-pause to network-pause.
        _onlineManager.SetOnline(true);

        var scope = new MutationScope("network-scope");
        var predecessorGate = new TaskCompletionSource<string>();

        // First mutation holds the scope gate open
        var obs1 = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (input, ctx, ct) => await predecessorGate.Task,
                NetworkMode = NetworkMode.Online
            });

        // Second mutation: scoped + NetworkMode.Online — will be scope-paused
        // first, then network-paused after going offline
        var secondExecuted = new TaskCompletionSource<string>();
        var obs2 = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = (input, ctx, ct) =>
                {
                    secondExecuted.TrySetResult("second-done");
                    return Task.FromResult("second-done");
                },
                NetworkMode = NetworkMode.Online
            });

        // Track pause transitions on the second observer. The first pause is from
        // scope-wait; the second pause (after going offline and releasing the gate)
        // is from network-pause.
        var pauseCount = 0;
        var scopePaused = new TaskCompletionSource();
        var networkPaused = new TaskCompletionSource();
        using var sub2 = obs2.Subscribe(result =>
        {
            if (result.IsPaused)
            {
                var count = Interlocked.Increment(ref pauseCount);
                if (count == 1) scopePaused.TrySetResult();
                else if (count >= 2) networkPaused.TrySetResult();
            }
        });

        var task1 = obs1.MutateAsync("first");
        var task2 = obs2.MutateAsync("second");

        // Wait for obs2 to become scope-paused deterministically
        await scopePaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(obs2.GetCurrentResult().IsPaused);

        // Go offline BEFORE releasing the scope gate — when the scope gate
        // opens, the second mutation's retryer will see it can't start
        // (offline) and enter network-pause.
        _onlineManager.SetOnline(false);

        // Release the scope gate — obs1 completes, obs2's scope-pause ends
        predecessorGate.SetResult("first-done");
        await task1.WaitAsync(TimeSpan.FromSeconds(5));

        // Wait for the second pause (network-pause) after the scope gate opens.
        // The retryer sees it's offline and pauses again.
        await networkPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(obs2.GetCurrentResult().IsPaused);

        // Go online — second mutation's network-pause ends and it executes
        _onlineManager.SetOnline(true);

        await secondExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await task2.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — both completed successfully
        var result2 = obs2.GetCurrentResult();
        Assert.True(result2.IsSuccess);
        Assert.Equal("second-done", result2.Data);
    }

    [Fact(Timeout = 10_000)]
    public async Task Mutation_CancelledWhilePaused_DoesNotTriggerErrorPath()
    {
        // Arrange — go offline, start a mutation with NetworkMode.Online so it pauses
        _onlineManager.SetOnline(false);

        var observer = new MutationObserver<string, Exception, string, object?>(
            _client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = (input, ctx, ct) => Task.FromResult("should-not-reach"),
                NetworkMode = NetworkMode.Online
            });

        var pausedTcs = new TaskCompletionSource();
        using var subscription = observer.Subscribe(result =>
        {
            if (result.IsPaused) pausedTcs.TrySetResult();
        });

        using var cts = new CancellationTokenSource();
        var mutateTask = observer.MutateAsync("test", cancellationToken: cts.Token);

        // Wait for the mutation to enter paused state
        await pausedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observer.GetCurrentResult().IsPaused);

        // Act — cancel the mutation while it's paused
        cts.Cancel();

        // Assert — MutateAsync throws OperationCanceledException (or the derived
        // TaskCanceledException — both indicate cancellation, not an error).
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mutateTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // The observer result should NOT show IsError — cancellation is not an error
        var finalResult = observer.GetCurrentResult();
        Assert.False(finalResult.IsError);

        // Clean up — go online so no lingering paused mutation remains
        _onlineManager.SetOnline(true);
    }

    #endregion
}
