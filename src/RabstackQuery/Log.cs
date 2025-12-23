using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Source-generated log messages for the RabstackQuery core library.
/// Uses <see cref="LoggerMessageAttribute"/> for zero-allocation logging.
/// Event ID ranges are documented in CLAUDE.md.
/// </summary>
internal static partial class Log
{
    // ── QueryClient: Information (10xx) ──────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, EventId = 1001,
        Message = "FetchQuery for key {QueryHash}")]
    public static partial void FetchQuery(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1002,
        Message = "PrefetchQuery for key {QueryHash}")]
    public static partial void PrefetchQuery(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1003,
        Message = "EnsureQueryData for key {QueryHash}")]
    public static partial void EnsureQueryData(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1004,
        Message = "SetQueryData for key {QueryHash}, query existed: {Existed}")]
    public static partial void SetQueryData(this ILogger logger, string queryHash, bool existed);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1005,
        Message = "InvalidateQueries")]
    public static partial void InvalidateQueries(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1006,
        Message = "RefetchQueries")]
    public static partial void RefetchQueries(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1007,
        Message = "RemoveQueries")]
    public static partial void RemoveQueries(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1008,
        Message = "ResetQueries")]
    public static partial void ResetQueries(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1009,
        Message = "CancelQueries")]
    public static partial void CancelQueries(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1010,
        Message = "FetchInfiniteQuery for key {QueryHash}")]
    public static partial void FetchInfiniteQuery(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1011,
        Message = "PrefetchInfiniteQuery for key {QueryHash}")]
    public static partial void PrefetchInfiniteQuery(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1012,
        Message = "EnsureInfiniteQueryData for key {QueryHash}")]
    public static partial void EnsureInfiniteQueryData(this ILogger logger, string queryHash);

    // ── Query: Information (12xx) ────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, EventId = 1201,
        Message = "Query {QueryHash} fetch started")]
    public static partial void QueryFetchStarted(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1202,
        Message = "Query {QueryHash} fetch completed successfully")]
    public static partial void QueryFetchSucceeded(this ILogger logger, string? queryHash);

    // ── Mutation: Information (14xx) ─────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, EventId = 1401,
        Message = "Mutation {MutationId} execute started")]
    public static partial void MutationExecuteStarted(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Information, EventId = 1402,
        Message = "Mutation {MutationId} execute completed successfully")]
    public static partial void MutationExecuteSucceeded(this ILogger logger, int mutationId);

    // ── QueryClient: Debug (20xx) ────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2001,
        Message = "EnsureQueryData cache hit for key {QueryHash}")]
    public static partial void EnsureQueryDataCacheHit(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2002,
        Message = "FetchQuery cache hit for key {QueryHash}")]
    public static partial void FetchQueryCacheHit(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2003,
        Message = "Focus changed, isFocused: {IsFocused}")]
    public static partial void FocusChanged(this ILogger logger, bool isFocused);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2004,
        Message = "Online changed, isOnline: {IsOnline}")]
    public static partial void OnlineChanged(this ILogger logger, bool isOnline);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2005,
        Message = "FetchInfiniteQuery cache hit for key {QueryHash}")]
    public static partial void FetchInfiniteQueryCacheHit(this ILogger logger, string queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2006,
        Message = "EnsureInfiniteQueryData cache hit for key {QueryHash}")]
    public static partial void EnsureInfiniteQueryDataCacheHit(this ILogger logger, string queryHash);

    // ── QueryCache: Debug (21xx) ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2101,
        Message = "QueryCache build: key {QueryHash}, created new: {CreatedNew}")]
    public static partial void QueryCacheBuild(this ILogger logger, string queryHash, bool createdNew);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2102,
        Message = "QueryCache add: key {QueryHash}")]
    public static partial void QueryCacheAdd(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2103,
        Message = "QueryCache remove: key {QueryHash}")]
    public static partial void QueryCacheRemove(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2104,
        Message = "QueryCache clear")]
    public static partial void QueryCacheClear(this ILogger logger);

    // ── Query: Debug (22xx) ──────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2201,
        Message = "Query {QueryHash} dispatch {ActionName}")]
    public static partial void QueryDispatch(this ILogger logger, string? queryHash, string actionName);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2202,
        Message = "Query {QueryHash} fetch deduplicated (already in-flight)")]
    public static partial void QueryFetchDeduplicated(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2203,
        Message = "Query {QueryHash} cancelled, revert: {Revert}")]
    public static partial void QueryCancelled(this ILogger logger, string? queryHash, bool revert);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2204,
        Message = "Query {QueryHash} reset")]
    public static partial void QueryReset(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2205,
        Message = "Query {QueryHash} invalidated")]
    public static partial void QueryInvalidated(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2206,
        Message = "Query {QueryHash} GC removed (no observers, idle)")]
    public static partial void QueryGcRemoved(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2207,
        Message = "Query {QueryHash} onFocus refetch triggered")]
    public static partial void QueryOnFocusRefetch(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2208,
        Message = "Query {QueryHash} onOnline refetch triggered")]
    public static partial void QueryOnOnlineRefetch(this ILogger logger, string? queryHash);

    // ── QueryObserver: Debug (23xx) ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2301,
        Message = "Observer subscribed to query {QueryHash}, listener count: {ListenerCount}")]
    public static partial void ObserverSubscribed(this ILogger logger, string? queryHash, int listenerCount);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2302,
        Message = "Observer unsubscribed from query {QueryHash}, listener count: {ListenerCount}")]
    public static partial void ObserverUnsubscribed(this ILogger logger, string? queryHash, int listenerCount);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2303,
        Message = "Observer query key changed from {OldHash} to {NewHash}")]
    public static partial void ObserverKeyChanged(this ILogger logger, string oldHash, string newHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2304,
        Message = "Observer refetch interval started: {Interval} for query {QueryHash}")]
    public static partial void ObserverRefetchIntervalStarted(this ILogger logger, TimeSpan interval, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2305,
        Message = "Observer refetch interval cleared for query {QueryHash}")]
    public static partial void ObserverRefetchIntervalCleared(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2306,
        Message = "Observer fetch on mount for query {QueryHash}, stale: {IsStale}")]
    public static partial void ObserverFetchOnMount(this ILogger logger, string? queryHash, bool isStale);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2307,
        Message = "Observer auto-refetch triggered for query {QueryHash} (invalidation)")]
    public static partial void ObserverAutoRefetch(this ILogger logger, string? queryHash);

    // ── Mutation: Debug (24xx) ───────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2401,
        Message = "Mutation {MutationId} OnMutate invoked")]
    public static partial void MutationOnMutateInvoked(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2402,
        Message = "Mutation {MutationId} GC removed")]
    public static partial void MutationGcRemoved(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2403,
        Message = "Mutation {MutationId} network paused")]
    public static partial void MutationNetworkPaused(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2404,
        Message = "Mutation {MutationId} network resumed")]
    public static partial void MutationNetworkResumed(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2405,
        Message = "Mutation {MutationId} cancelled")]
    public static partial void MutationCancelled(this ILogger logger, int mutationId);

    // ── MutationObserver: Debug (25xx) ───────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2501,
        Message = "MutationObserver created mutation {MutationId}")]
    public static partial void MutationObserverCreated(this ILogger logger, int mutationId);

    // ── MutationCache: Debug (26xx) ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2601,
        Message = "MutationCache build: mutation {MutationId}")]
    public static partial void MutationCacheBuild(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2602,
        Message = "MutationCache remove: mutation {MutationId}")]
    public static partial void MutationCacheRemove(this ILogger logger, int mutationId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2603,
        Message = "MutationCache resuming {Count} paused mutations")]
    public static partial void MutationCacheResumingPaused(this ILogger logger, int count);

    // ── Retryer: Debug (27xx) ────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2701,
        Message = "Retryer executing attempt {Attempt}")]
    public static partial void RetryerAttempt(this ILogger logger, int attempt);

    // ── QueryClient: Warning (30xx) ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3001,
        Message = "PrefetchQuery error swallowed for key {QueryHash}")]
    public static partial void PrefetchQueryErrorSwallowed(this ILogger logger, string queryHash, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3002,
        Message = "ResumePausedMutations error swallowed")]
    public static partial void ResumePausedMutationsErrorSwallowed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3004,
        Message = "PrefetchInfiniteQuery error swallowed for key {QueryHash}")]
    public static partial void PrefetchInfiniteQueryErrorSwallowed(this ILogger logger, string queryHash, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3003,
        Message = "Event handler error swallowed during {EventName}")]
    public static partial void EventHandlerErrorSwallowed(this ILogger logger, string eventName, Exception exception);

    // ── Query: Warning (32xx) ────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3201,
        Message = "Query {QueryHash} fetch cancelled")]
    public static partial void QueryFetchCancelled(this ILogger logger, string? queryHash);

    // ── Mutation: Warning (34xx) ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3401,
        Message = "Mutation {MutationId} OnError callback threw")]
    public static partial void MutationOnErrorCallbackThrew(this ILogger logger, int mutationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3402,
        Message = "Mutation {MutationId} OnSettled callback threw")]
    public static partial void MutationOnSettledCallbackThrew(this ILogger logger, int mutationId, Exception exception);

    // ── MutationCache: Warning (36xx) ──────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3601,
        Message = "MutationCache failed to resume mutation {MutationId}")]
    public static partial void MutationCacheResumeError(this ILogger logger, int mutationId, Exception exception);

    // ── Retryer: Warning (37xx) ──────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3701,
        Message = "Retryer attempt {Attempt} failed, retrying after {Delay}")]
    public static partial void RetryerRetrying(this ILogger logger, int attempt, TimeSpan delay, Exception exception);

    // ── Query: Error (42xx) ──────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error, EventId = 4201,
        Message = "Query {QueryHash} fetch failed permanently after {FailureCount} attempts")]
    public static partial void QueryFetchFailed(this ILogger logger, string? queryHash, int failureCount, Exception exception);

    // ── Mutation: Error (44xx) ───────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error, EventId = 4401,
        Message = "Mutation {MutationId} failed permanently")]
    public static partial void MutationFailed(this ILogger logger, int mutationId, Exception exception);
}
