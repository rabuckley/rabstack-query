using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

/// <summary>
/// Executes an async function with configurable retry count, exponential backoff,
/// and automatic pause/resume based on network connectivity and window focus.
/// </summary>
public sealed class Retryer<TData> : IDisposable
{
    private readonly RetryerOptions<TData> _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private int _failureCount;

    // Unlike retryer.ts createRetryer's single-threaded state, these fields cross
    // event-handler and async-continuation threads. Volatile access makes retry
    // cancellation and pause callbacks visible to those threads.

    /// <summary>
    /// When true, the next retry is prevented but the current attempt is not
    /// cancelled. Set by <see cref="CancelRetry"/>, cleared by
    /// <see cref="ContinueRetry"/>. Used when the last observer unmounts.
    /// </summary>
    private volatile bool _isRetryCancelled;

    /// <summary>
    /// Callback to resolve the pause gate. Set during <see cref="PauseAsync"/>,
    /// invoked by <see cref="Continue"/> to wake a paused retryer.
    /// </summary>
    private volatile Action? _continueFn;

    public Retryer(RetryerOptions<TData> options)
    {
        _options = options;
        _logger = (options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RabstackQuery.Retryer");
        _cts = new CancellationTokenSource();
        _failureCount = 0;
    }

    public void Dispose()
    {
        // CancellationTokenSource.Dispose() does NOT trigger cancellation.
        // If a retryer is paused (awaiting a TCS) and disposed without
        // cancellation, the TCS and its continuation task are leaked forever.
        Cancel();
        _cts.Dispose();
    }

    public bool IsCancelled => _cts.IsCancellationRequested;

    // ── Public pause / continue API ────────────────────────────────

    /// <summary>
    /// Prevents the next retry attempt without cancelling the current one.
    /// Called by <c>Query.RemoveObserver</c> when the last observer leaves.
    /// Mirrors TanStack's <c>retryer.cancelRetry()</c>.
    /// </summary>
    public void CancelRetry() => _isRetryCancelled = true;

    /// <summary>
    /// Clears the cancel-retry flag so retries can resume. Called from the
    /// fetch deduplication path (<c>Query.Fetch()</c>) when a new caller joins
    /// an in-flight fetch that had its retries cancelled by a prior
    /// <see cref="CancelRetry"/>. Mirrors TanStack's <c>retryer.continueRetry()</c>.
    /// </summary>
    public void ContinueRetry() => _isRetryCancelled = false;

    /// <summary>
    /// Wakes a paused retryer by invoking the continuation callback. Called
    /// from <c>Query.OnFocus()</c> and <c>Query.OnOnline()</c> to interrupt
    /// pause waits when connectivity/focus is restored. Mirrors TanStack's
    /// <c>retryer.continue()</c>.
    /// </summary>
    public void Continue() => _continueFn?.Invoke();

    /// <summary>
    /// Whether execution can start: network is suitable and the <c>canRun</c>
    /// delegate allows it. Mirrors TanStack's <c>retryer.canStart()</c>.
    /// </summary>
    public bool CanStart()
    {
        var canRun = _options.CanRun?.Invoke() ?? true;
        return NetworkModeHelper.CanFetch(_options.NetworkMode, GetOnlineManager()) && canRun;
    }

    /// <summary>
    /// Executes the function with retry logic and exponential backoff.
    /// If the network is unavailable at start, pauses until connectivity
    /// is restored before entering the retry loop.
    /// </summary>
    public async Task<TData> ExecuteAsync()
    {
        if (!CanStart())
        {
            await PauseAsync();
        }

        while (!IsCancelled)
        {
            try
            {
                _logger.RetryerAttempt(_failureCount + 1);
                return await _options.Fn(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // If our cancellation token was cancelled, propagate it
                throw;
            }
            catch (Exception ex) when (!IsCancelled)
            {
                _failureCount++;

                if (_isRetryCancelled || _failureCount >= _options.MaxRetries)
                {
                    throw;
                }

                // Notify about the failure
                _options.OnFail?.Invoke(_failureCount, ex);

                _options.Metrics?.RetryTotal?.Add(1,
                    QueryMetrics.RetrySourceTag(_options.RetrySource ?? "unknown"));

                // Calculate delay before next retry
                var delay = _options.RetryDelay?.Invoke(_failureCount, ex)
                    ?? DefaultRetryDelay(_failureCount);

                _logger.RetryerRetrying(_failureCount, delay, ex);

                // Wait before retrying using TimeProvider so tests can
                // advance time deterministically via FakeTimeProvider.
                try
                {
                    await DelayAsync(delay, _options.TimeProvider, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // If cancelled during delay, exit
                    throw;
                }

                if (!CanContinue())
                {
                    await PauseAsync();
                }

                if (_isRetryCancelled)
                {
                    throw;
                }
            }
        }

        throw new OperationCanceledException("Retry operation was cancelled");
    }

    /// <summary>
    /// Cancels the retry operation.
    /// </summary>
    public void Cancel()
    {
        if (!_cts.IsCancellationRequested)
        {
            // PauseAsync registers a cancellation callback on _cts.Token that
            // calls tcs.TrySetCanceled(), which wakes the pause. No need to
            // invoke _continueFn here — the registration handles it, and
            // _continueFn's gate check (IsCancelled || CanContinue()) would
            // fail anyway since the CTS hasn't been cancelled yet at that point.
            _cts.Cancel();
        }
    }

    // ── Private helpers ────────────────────────────────────────────

    private bool CanContinue()
    {
        var focusManager = _options.FocusManager;
        var isFocused = focusManager?.IsFocused ?? true;
        var onlineManager = GetOnlineManager();
        var isOnlineSuitable = _options.NetworkMode is NetworkMode.Always || onlineManager.IsOnline;
        var canRun = _options.CanRun?.Invoke() ?? true;

        return isFocused && isOnlineSuitable && canRun;
    }

    private async Task PauseAsync()
    {
        // RunContinuationsAsynchronously prevents Continue() (called from an
        // event-handler thread, e.g. OnlineChanged → OnOnline) from running
        // the entire retry loop inline on that thread, blocking other queries
        // from being woken.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _continueFn = () =>
        {
            if (IsCancelled || CanContinue())
            {
                tcs.TrySetResult();
            }
        };

        _options.OnPause?.Invoke();

        // Cancellation wakes the pause so ExecuteAsync can observe and rethrow.
        await using var registration = _cts.Token.Register(() => tcs.TrySetCanceled(_cts.Token));

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Let ExecuteAsync handle cancellation normally.
            throw;
        }
        finally
        {
            _continueFn = null;
        }

        if (!IsCancelled)
        {
            _options.OnContinue?.Invoke();
        }
    }

    private IOnlineManager GetOnlineManager()
        => _options.OnlineManager ?? OnlineManager.Instance;

    /// <summary>
    /// Default exponential backoff strategy: 1s, 2s, 4s, 8s, max 30s.
    /// </summary>
    private static TimeSpan DefaultRetryDelay(int failureCount)
    {
        var ms = Math.Min(1000 * (int)Math.Pow(2, failureCount - 1), 30000);
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> replacement that routes
    /// through a <see cref="TimeProvider"/> so that <c>FakeTimeProvider</c> can advance
    /// the delay deterministically in tests. The BCL doesn't provide a
    /// <c>TimeProvider.Delay</c> extension, so we build one from <c>CreateTimer</c>.
    /// </summary>
    private static async Task DelayAsync(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // RunContinuationsAsynchronously prevents the timer callback from
        // running the entire retry loop inline on the timer thread.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register cancellation before creating the timer so the cancellation
        // path is wired before the timer can fire (matters when delay is zero).
        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        using var timer = timeProvider.CreateTimer(_ => tcs.TrySetResult(), null, delay, Timeout.InfiniteTimeSpan);

        await tcs.Task;
    }
}
