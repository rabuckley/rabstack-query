using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Configuration for a <see cref="Retryer{TData}"/>, specifying the function to
/// execute, retry limits, delay strategy, and network/focus awareness callbacks.
/// </summary>
public sealed class RetryerOptions<TData>
{
    /// <summary>
    /// The function to execute with retry logic.
    /// </summary>
    public required Func<CancellationToken, Task<TData>> Fn { get; init; }

    /// <summary>
    /// Maximum number of retry attempts. Default is 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Optional custom delay function. Receives failure count and exception.
    /// Returns delay as a <see cref="TimeSpan"/>.
    /// </summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    /// Optional callback invoked on each failure.
    /// </summary>
    public Action<int, Exception>? OnFail { get; init; }

    /// <summary>
    /// Optional callback invoked when execution is paused (offline/unfocused).
    /// Mirrors TanStack's <c>RetryerConfig.onPause</c>. Queries dispatch
    /// <see cref="PauseAction"/> here to set <see cref="FetchStatus.Paused"/>.
    /// </summary>
    public Action? OnPause { get; init; }

    /// <summary>
    /// Optional callback invoked when execution continues after pause.
    /// Mirrors TanStack's <c>RetryerConfig.onContinue</c>. Queries dispatch
    /// <see cref="ContinueAction"/> here to set <see cref="FetchStatus.Fetching"/>.
    /// </summary>
    public Action? OnContinue { get; init; }

    /// <summary>
    /// TimeProvider for scheduling retry delays.
    /// </summary>
    public required TimeProvider TimeProvider { get; init; }

    /// <summary>
    /// LoggerFactory for creating the retryer's logger.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Metrics instrumentation. Null when metrics are disabled.
    /// </summary>
    internal QueryMetrics? Metrics { get; init; }

    /// <summary>
    /// Identifies the subsystem that created this retryer (<c>"query"</c> or
    /// <c>"mutation"</c>). Used as the <c>rabstackquery.retry.source</c> tag
    /// on the retry counter metric.
    /// </summary>
    internal string? RetrySource { get; init; }

    // ── Network mode support ───────────────────────────────────────

    /// <summary>
    /// Controls whether execution is gated on network connectivity.
    /// Defaults to <see cref="NetworkMode.Online"/>. Mirrors TanStack's
    /// <c>RetryerConfig.networkMode</c>.
    /// </summary>
    public NetworkMode NetworkMode { get; init; } = NetworkMode.Online;

    /// <summary>
    /// Delegate that gates whether the retryer is allowed to run.
    /// Queries always pass <c>() => true</c>; mutations use this for
    /// scope coordination via <see cref="MutationCache"/>. Mirrors
    /// TanStack's <c>RetryerConfig.canRun</c>.
    /// </summary>
    public Func<bool>? CanRun { get; init; }

    /// <summary>
    /// Online manager for checking network state during pause/continue
    /// decisions. Required when <see cref="NetworkMode"/> is
    /// <see cref="NetworkMode.Online"/>.
    /// </summary>
    public IOnlineManager? OnlineManager { get; init; }

    /// <summary>
    /// Focus manager for checking app focus state during continue decisions.
    /// TanStack's <c>canContinue()</c> requires focus before resuming.
    /// </summary>
    public IFocusManager? FocusManager { get; init; }
}
