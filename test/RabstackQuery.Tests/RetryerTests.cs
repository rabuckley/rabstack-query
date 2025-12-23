namespace RabstackQuery.Tests;

/// <summary>
/// Dedicated unit tests for <see cref="Retryer{TData}"/> pause, continue,
/// and cancelRetry semantics. Mirrors TanStack's retryer behavior from
/// <c>retryer.ts</c>.
/// </summary>
public sealed class RetryerTests
{
    #region CancelRetry / ContinueRetry

    [Fact(Timeout = 10_000)]
    public async Task CancelRetry_PreventsNextRetry_ButCurrentAttemptFinishes()
    {
        // Arrange — a retryer configured with 3 retries. The first attempt
        // always fails. We call CancelRetry() after the first failure, so the
        // retryer should NOT attempt a second try.
        var attemptCount = 0;
        var onlineManager = new OnlineManager();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                attemptCount++;
                throw new InvalidOperationException("fail");
            },
            MaxRetries = 4, // initial + 3 retries
            RetryDelay = (_, _) => TimeSpan.Zero,
            TimeProvider = TimeProvider.System,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnFail = (_, _) => { }
        });

        retryer.CancelRetry();

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => retryer.ExecuteAsync());

        // Assert — only one attempt, no retries
        Assert.Equal(1, attemptCount);
        Assert.Equal("fail", ex.Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task ContinueRetry_AllowsRetryAfterCancelRetry()
    {
        // Arrange
        var attemptCount = 0;
        var onlineManager = new OnlineManager();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                attemptCount++;
                if (attemptCount < 3)
                    throw new InvalidOperationException("fail");
                return Task.FromResult("success");
            },
            MaxRetries = 4, // initial + 3 retries
            RetryDelay = (_, _) => TimeSpan.Zero,
            TimeProvider = TimeProvider.System,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnFail = (_, _) => { }
        });

        // Cancel then immediately re-enable retries
        retryer.CancelRetry();
        retryer.ContinueRetry();

        // Act
        var result = await retryer.ExecuteAsync();

        // Assert — retries were allowed, eventually succeeded
        Assert.Equal("success", result);
        Assert.Equal(3, attemptCount);
    }

    /// <summary>
    /// Verifies at the Retryer level that CancelRetry prevents further retries.
    /// Moved from NetworkModeTests where it tested Query.RemoveObserver indirectly.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task CancelRetry_PreventsRetries_WhenCalledDuringBackoff()
    {
        // Arrange
        var onlineManager = new OnlineManager();
        var focusManager = new FocusManager();
        var attemptCount = 0;
        var firstAttemptDone = new TaskCompletionSource();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                Interlocked.Increment(ref attemptCount);
                if (attemptCount == 1)
                    firstAttemptDone.TrySetResult();
                throw new InvalidOperationException("always fails");
            },
            MaxRetries = 4, // initial + 3 retries
            RetryDelay = (_, _) => TimeSpan.FromMilliseconds(200),
            TimeProvider = TimeProvider.System,
            OnlineManager = onlineManager,
            FocusManager = focusManager,
            CanRun = () => true,
            OnFail = (_, _) => { }
        });

        // Start execution
        var executeTask = retryer.ExecuteAsync();
        await firstAttemptDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — CancelRetry prevents the next retry
        retryer.CancelRetry();

        // Assert — should throw after the delay completes and the flag is checked
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executeTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // Only 1 attempt: the initial one. CancelRetry prevented retries.
        Assert.Equal(1, attemptCount);
    }

    #endregion

    #region CanStart

    [Fact]
    public void CanStart_ReturnsFalse_WhenOfflineAndNetworkModeOnline()
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true
        });

        // Act & Assert
        Assert.False(retryer.CanStart());
    }

    [Fact]
    public void CanStart_ReturnsTrue_WhenOnlineAndNetworkModeOnline()
    {
        // Arrange
        var onlineManager = new OnlineManager();
        // Default is online

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true
        });

        // Act & Assert
        Assert.True(retryer.CanStart());
    }

    [Fact]
    public void CanStart_ReturnsTrue_WhenOfflineAndNetworkModeAlways()
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Always,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true
        });

        // Act & Assert
        Assert.True(retryer.CanStart());
    }

    [Fact]
    public void CanStart_ReturnsFalse_WhenCanRunReturnsFalse()
    {
        // Arrange
        var onlineManager = new OnlineManager();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => false
        });

        // Act & Assert
        Assert.False(retryer.CanStart());
    }

    #endregion

    #region Pause / Continue

    [Fact(Timeout = 10_000)]
    public async Task ExecuteAsync_PausesAtStart_WhenOffline_ResumesOnContinue()
    {
        // Arrange — start offline so ExecuteAsync enters PauseAsync before
        // the first attempt.
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);

        var pauseCalled = new TaskCompletionSource();
        var continueCalled = false;

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            MaxRetries = 1,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnPause = () => pauseCalled.TrySetResult(),
            OnContinue = () => continueCalled = true
        });

        // Act — start execution (will pause immediately)
        var task = retryer.ExecuteAsync();

        // Wait for the retryer to enter PauseAsync deterministically
        await pauseCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted, "Should still be paused");

        // Go online and call Continue to wake the paused retryer
        onlineManager.SetOnline(true);
        retryer.Continue();

        // Assert — should complete successfully
        var result = await task;
        Assert.Equal("data", result);
        Assert.True(continueCalled, "OnContinue should have been called");
    }

    [Fact(Timeout = 10_000)]
    public async Task PauseAsync_CallsOnPauseAndOnContinue_OnTransitions()
    {
        // Arrange — simulate going offline between retries
        var onlineManager = new OnlineManager();
        var focusManager = new FocusManager();
        var attemptCount = 0;
        var pauseCount = 0;
        var continueCount = 0;
        var pausedDuringRetry = new TaskCompletionSource();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    // First attempt fails. We'll go offline before the retry
                    // delay completes so the post-delay canContinue check fails.
                    throw new InvalidOperationException("transient");
                }
                return Task.FromResult("recovered");
            },
            MaxRetries = 3,
            RetryDelay = (_, _) => TimeSpan.Zero,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = focusManager,
            CanRun = () => true,
            OnPause = () =>
            {
                pauseCount++;
                pausedDuringRetry.TrySetResult();
            },
            OnContinue = () => continueCount++,
            OnFail = (_, _) =>
            {
                // Simulate losing network after the first failure
                onlineManager.SetOnline(false);
            }
        });

        // Act — start execution
        var task = retryer.ExecuteAsync();

        // Wait for the retryer to pause deterministically
        await pausedDuringRetry.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, pauseCount);

        // Resume by going online
        onlineManager.SetOnline(true);
        retryer.Continue();

        var result = await task;

        // Assert
        Assert.Equal("recovered", result);
        Assert.Equal(1, pauseCount);
        Assert.Equal(1, continueCount);
        Assert.Equal(2, attemptCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task Continue_WakesPausedRetryer_DuringBackoff()
    {
        // Arrange — a retryer that fails once, then the post-delay canContinue
        // check fails (offline), so it pauses. Continue() wakes it.
        var onlineManager = new OnlineManager();
        var attemptCount = 0;
        var paused = new TaskCompletionSource();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    throw new InvalidOperationException("fail");
                return Task.FromResult("ok");
            },
            MaxRetries = 3,
            RetryDelay = (_, _) => TimeSpan.Zero,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnPause = () => paused.TrySetResult(),
            OnFail = (_, _) =>
            {
                // Go offline after first failure so canContinue() returns false
                onlineManager.SetOnline(false);
            }
        });

        var task = retryer.ExecuteAsync();

        // Wait for the retryer to enter pause deterministically
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted, "Should be paused waiting for continue");

        // Act — go online and signal continue
        onlineManager.SetOnline(true);
        retryer.Continue();

        var result = await task;

        // Assert
        Assert.Equal("ok", result);
        Assert.Equal(2, attemptCount);
    }

    #endregion

    #region Cancel during pause

    [Fact(Timeout = 10_000)]
    public async Task Cancel_WakesPausedRetryer_AndThrowsCancellation()
    {
        // Arrange
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var paused = new TaskCompletionSource();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            MaxRetries = 1,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnPause = () => paused.TrySetResult()
        });

        // Act — start, will pause
        var task = retryer.ExecuteAsync();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        retryer.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    #endregion

    #region Continue edge cases

    [Fact(Timeout = 10_000)]
    public async Task Continue_WhenNoPauseActive_IsNoOp()
    {
        // Arrange — Continue() before any pause should not affect execution.
        var onlineManager = new OnlineManager();

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            MaxRetries = 1,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true
        });

        // Act — call Continue() before any pause
        retryer.Continue();

        // ExecuteAsync should still work normally
        var result = await retryer.ExecuteAsync();

        // Assert
        Assert.Equal("data", result);
    }

    [Fact(Timeout = 10_000)]
    public async Task Continue_CalledConcurrently_CompletesOnce()
    {
        // Arrange — a paused retryer receives multiple concurrent Continue()
        // calls. Only one should resolve the TCS; the rest are harmless no-ops.
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var paused = new TaskCompletionSource();
        var continueCount = 0;

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ => Task.FromResult("data"),
            MaxRetries = 1,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.Online,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnPause = () => paused.TrySetResult(),
            OnContinue = () => Interlocked.Increment(ref continueCount)
        });

        var task = retryer.ExecuteAsync();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — go online, then call Continue() concurrently
        onlineManager.SetOnline(true);
        Parallel.For(0, 10, _ => retryer.Continue());

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — task completed successfully, OnContinue called exactly once
        Assert.Equal("data", result);
        Assert.Equal(1, continueCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task OfflineFirst_StartsOffline_RetriesPauseUntilOnline()
    {
        // Arrange — OfflineFirst allows the initial attempt (CanStart=true even
        // when offline), but post-failure canContinue() requires online for
        // retries. Verify that CanStart is true but subsequent retries pause.
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        var paused = new TaskCompletionSource();
        var attemptCount = 0;

        var retryer = new Retryer<string>(new RetryerOptions<string>
        {
            Fn = _ =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    throw new InvalidOperationException("offline fail");
                return Task.FromResult("recovered");
            },
            MaxRetries = 3,
            RetryDelay = (_, _) => TimeSpan.Zero,
            TimeProvider = TimeProvider.System,
            NetworkMode = NetworkMode.OfflineFirst,
            OnlineManager = onlineManager,
            FocusManager = new FocusManager(),
            CanRun = () => true,
            OnPause = () => paused.TrySetResult(),
            OnFail = (_, _) => { }
        });

        // OfflineFirst allows starting even when offline
        Assert.True(retryer.CanStart());

        // Act — first attempt runs (fails), then post-delay pause because
        // CanContinue needs online (OfflineFirst only exempts CanStart/CanFetch).
        var task = retryer.ExecuteAsync();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, attemptCount);
        Assert.False(task.IsCompleted);

        // Go online and continue
        onlineManager.SetOnline(true);
        retryer.Continue();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("recovered", result);
        Assert.Equal(2, attemptCount);
    }

    #endregion
}
