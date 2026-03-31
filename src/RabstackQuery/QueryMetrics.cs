using System.Diagnostics.Metrics;

namespace RabstackQuery;

/// <summary>
/// Centralised metrics instrumentation for RabStack Query.
/// </summary>
/// <remarks>
/// All instruments are nullable — null when metrics are disabled (no
/// <see cref="IMeterFactory"/> provided, or <c>Meter.IsSupported</c> is <c>false</c>
/// at trim time). Every recording call uses <c>instrument?.Add(...)</c> or
/// <c>instrument?.Record(...)</c>, which compiles to a single null check.
///
/// The <see cref="RuntimeFeature.IsMeterSupported"/> guard in the constructor
/// allows the trimmer to strip all <see cref="System.Diagnostics.Metrics"/> usage
/// when the <c>System.Diagnostics.Metrics.Meter.IsSupported</c> feature switch
/// is set to <c>false</c>.
/// </remarks>
internal sealed class QueryMetrics
{
    internal const string MeterName = "RabstackQuery";

    // ── Query Fetch ─────────────────────────────────────────

    internal Counter<long>? QueryFetchTotal { get; }
    internal Counter<long>? QueryFetchSuccessTotal { get; }
    internal Counter<long>? QueryFetchErrorTotal { get; }
    internal Counter<long>? QueryFetchCancelledTotal { get; }
    internal Counter<long>? QueryFetchDeduplicatedTotal { get; }
    internal Histogram<double>? QueryFetchDuration { get; }

    // ── Cache ───────────────────────────────────────────────

    internal Counter<long>? CacheHitTotal { get; }
    internal Counter<long>? CacheMissTotal { get; }
    internal UpDownCounter<long>? CacheSize { get; }
    internal Counter<long>? CacheGcRemovedTotal { get; }

    // ── Mutation ────────────────────────────────────────────

    internal Counter<long>? MutationTotal { get; }
    internal Counter<long>? MutationSuccessTotal { get; }
    internal Counter<long>? MutationErrorTotal { get; }
    internal Counter<long>? MutationCancelledTotal { get; }
    internal Histogram<double>? MutationDuration { get; }

    // ── Retry ───────────────────────────────────────────────

    internal Counter<long>? RetryTotal { get; }

    // ── Observer ────────────────────────────────────────────

    internal UpDownCounter<long>? ActiveObservers { get; }

    // ── Invalidation & Refetch ──────────────────────────────

    internal Counter<long>? InvalidationTotal { get; }
    internal Counter<long>? RefetchOnFocusTotal { get; }
    internal Counter<long>? RefetchOnReconnectTotal { get; }

    // ── Tag Helpers ─────────────────────────────────────────

    internal static KeyValuePair<string, object?> QueryHashTag(string? hash) =>
        new("rabstackquery.query.hash", hash ?? "unknown");

    internal static KeyValuePair<string, object?> MutationKeyTag(string? key) =>
        new("rabstackquery.mutation.key", key ?? "unknown");

    internal static KeyValuePair<string, object?> RetrySourceTag(string source) =>
        new("rabstackquery.retry.source", source);

    // ── Construction ────────────────────────────────────────

    /// <summary>
    /// Histogram bucket boundaries targeting typical API call durations (10ms–10s)
    /// with good resolution in the 50–500ms range where most queries land.
    /// </summary>
    private static readonly double[] FetchDurationBuckets =
        [0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    public QueryMetrics(IMeterFactory? meterFactory)
    {
        if (!RuntimeFeature.IsMeterSupported || meterFactory is null)
            return;

        var meter = meterFactory.Create(MeterName);

        // Query Fetch
        QueryFetchTotal = meter.CreateCounter<long>(
            "rabstackquery.query.fetch_total", "{queries}",
            "Total query fetches started (excludes deduplicated)");

        QueryFetchSuccessTotal = meter.CreateCounter<long>(
            "rabstackquery.query.fetch_success_total", "{queries}",
            "Fetches that completed successfully");

        QueryFetchErrorTotal = meter.CreateCounter<long>(
            "rabstackquery.query.fetch_error_total", "{queries}",
            "Fetches that failed permanently (after all retries)");

        QueryFetchCancelledTotal = meter.CreateCounter<long>(
            "rabstackquery.query.fetch_cancelled_total", "{queries}",
            "Fetches cancelled via CancellationToken or Cancel()");

        QueryFetchDeduplicatedTotal = meter.CreateCounter<long>(
            "rabstackquery.query.fetch_deduplicated_total", "{queries}",
            "Fetches skipped because an identical fetch was already in-flight");

        QueryFetchDuration = meter.CreateHistogram(
            "rabstackquery.query.fetch_duration", "s",
            "Wall-clock duration of fetches (success and error, excludes cancelled)",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FetchDurationBuckets });

        // Cache
        CacheHitTotal = meter.CreateCounter<long>(
            "rabstackquery.cache.hit_total", "{queries}",
            "Cache hits (data fresh, fetch skipped)");

        CacheMissTotal = meter.CreateCounter<long>(
            "rabstackquery.cache.miss_total", "{queries}",
            "Cache misses (data stale or absent, fetch required)");

        CacheSize = meter.CreateUpDownCounter<long>(
            "rabstackquery.cache.size", "{queries}",
            "Current number of queries in the cache");

        CacheGcRemovedTotal = meter.CreateCounter<long>(
            "rabstackquery.cache.gc_removed_total", "{queries}",
            "Queries removed by garbage collection (GcTime expired, no observers)");

        // Mutation
        MutationTotal = meter.CreateCounter<long>(
            "rabstackquery.mutation.total", "{mutations}",
            "Total mutations started");

        MutationSuccessTotal = meter.CreateCounter<long>(
            "rabstackquery.mutation.success_total", "{mutations}",
            "Mutations completed successfully");

        MutationErrorTotal = meter.CreateCounter<long>(
            "rabstackquery.mutation.error_total", "{mutations}",
            "Mutations failed permanently");

        MutationCancelledTotal = meter.CreateCounter<long>(
            "rabstackquery.mutation.cancelled_total", "{mutations}",
            "Mutations cancelled via CancellationToken");

        MutationDuration = meter.CreateHistogram(
            "rabstackquery.mutation.duration", "s",
            "Wall-clock duration from Execute() start to completion",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FetchDurationBuckets });

        // Retry
        RetryTotal = meter.CreateCounter<long>(
            "rabstackquery.retry.total", "{retries}",
            "Total retry attempts (each individual retry, not each retried query)");

        // Observer
        ActiveObservers = meter.CreateUpDownCounter<long>(
            "rabstackquery.observer.active", "{observers}",
            "Current number of active observers");

        // Invalidation & Refetch
        InvalidationTotal = meter.CreateCounter<long>(
            "rabstackquery.query.invalidation_total", "{queries}",
            "Total query invalidations");

        RefetchOnFocusTotal = meter.CreateCounter<long>(
            "rabstackquery.query.refetch_on_focus_total", "{queries}",
            "Refetches triggered by window focus");

        RefetchOnReconnectTotal = meter.CreateCounter<long>(
            "rabstackquery.query.refetch_on_reconnect_total", "{queries}",
            "Refetches triggered by network reconnection");
    }
}
