using Microsoft.Extensions.Logging;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Source-generated log messages for the RabstackQuery.Mvvm library.
/// Uses <see cref="LoggerMessageAttribute"/> for zero-allocation logging.
/// Event ID ranges are documented in CLAUDE.md.
/// </summary>
internal static partial class Log
{
    // ── QueryViewModel: Debug (61xx) ─────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6101,
        Message = "QueryViewModel subscribed to query {QueryHash}")]
    public static partial void QueryViewModelSubscribed(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6102,
        Message = "QueryViewModel disposed for query {QueryHash}")]
    public static partial void QueryViewModelDisposed(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6103,
        Message = "QueryViewModel result updated for query {QueryHash}, status: {Status}")]
    public static partial void QueryViewModelResultUpdated(this ILogger logger, string? queryHash, QueryStatus status);

    // ── QueryViewModel: Warning (61xx) ───────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 6104,
        Message = "QueryViewModel refetch error swallowed for query {QueryHash}")]
    public static partial void QueryViewModelRefetchErrorSwallowed(this ILogger logger, string? queryHash, Exception exception);

    // ── MutationViewModel: Debug (62xx) ──────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6201,
        Message = "MutationViewModel created")]
    public static partial void MutationViewModelCreated(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6202,
        Message = "MutationViewModel disposed")]
    public static partial void MutationViewModelDisposed(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6203,
        Message = "MutationViewModel result updated, status: {Status}")]
    public static partial void MutationViewModelResultUpdated(this ILogger logger, MutationStatus status);

    // ── MutationViewModel: Warning (62xx) ────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 6204,
        Message = "MutationViewModel mutate error swallowed")]
    public static partial void MutationViewModelMutateErrorSwallowed(this ILogger logger, Exception exception);

    // ── InfiniteQueryViewModel: Debug (64xx) ──────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6401,
        Message = "InfiniteQueryViewModel subscribed to query {QueryHash}")]
    public static partial void InfiniteQueryViewModelSubscribed(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6402,
        Message = "InfiniteQueryViewModel disposed for query {QueryHash}")]
    public static partial void InfiniteQueryViewModelDisposed(this ILogger logger, string? queryHash);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6403,
        Message = "InfiniteQueryViewModel result updated for query {QueryHash}, status: {Status}")]
    public static partial void InfiniteQueryViewModelResultUpdated(this ILogger logger, string? queryHash, QueryStatus status);

    // ── InfiniteQueryViewModel: Warning (64xx) ──────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, EventId = 6404,
        Message = "InfiniteQueryViewModel fetch page error swallowed for query {QueryHash}")]
    public static partial void InfiniteQueryViewModelFetchPageErrorSwallowed(this ILogger logger, string? queryHash, Exception exception);

    // ── QueryCollectionViewModel: Debug (63xx) ───────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6301,
        Message = "QueryCollectionViewModel updating items")]
    public static partial void QueryCollectionViewModelUpdatingItems(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6302,
        Message = "QueryCollectionViewModel disposed")]
    public static partial void QueryCollectionViewModelDisposed(this ILogger logger);
}
