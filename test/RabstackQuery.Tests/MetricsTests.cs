using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery.Tests;

/// <summary>
/// Tests for the metrics instrumentation across the entire RabStack Query pipeline.
/// Each test creates an isolated <see cref="IMeterFactory"/> and uses
/// <see cref="MetricCollector{T}"/> to assert on measurements without cross-test
/// interference.
/// </summary>
public sealed class MetricsTests
{
    private static (QueryClient Client, IMeterFactory MeterFactory) CreateClientWithMetrics(
        FakeTimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        var sp = services.BuildServiceProvider();
        var meterFactory = sp.GetRequiredService<IMeterFactory>();

        var client = new QueryClient(
            new QueryCache(),
            timeProvider: timeProvider,
            meterFactory: meterFactory);

        return (client, meterFactory);
    }

    // ── Query Fetch: Success ────────────────────────────────────────

    [Fact]
    public async Task Fetch_RecordsSuccessMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var fetchTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_total");
        var fetchSuccess = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_success_total");
        var fetchDuration = new MetricCollector<double>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_duration");

        // Act
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "result"
        });

        // Assert
        var fetches = fetchTotal.GetMeasurementSnapshot();
        Assert.Single(fetches);
        Assert.Equal(1, fetches[0].Value);

        var expectedHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["test"]);
        Assert.Equal(expectedHash, fetches[0].Tags["rabstackquery.query.hash"]);

        Assert.Single(fetchSuccess.GetMeasurementSnapshot());

        var durations = fetchDuration.GetMeasurementSnapshot();
        Assert.Single(durations);
        Assert.True(durations[0].Value > 0);
    }

    // ── Query Fetch: Error ──────────────────────────────────────────

    [Fact]
    public async Task Fetch_RecordsErrorMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var fetchTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_total");
        var fetchError = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_error_total");
        var fetchDuration = new MetricCollector<double>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_duration");

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.FetchQueryAsync(new FetchQueryOptions<string>
            {
                QueryKey = ["fail"],
                QueryFn = _ => throw new InvalidOperationException("boom")
            }));

        // Assert
        Assert.Single(fetchTotal.GetMeasurementSnapshot());
        Assert.Single(fetchError.GetMeasurementSnapshot());

        var durations = fetchDuration.GetMeasurementSnapshot();
        Assert.Single(durations);
        Assert.True(durations[0].Value >= 0);
    }

    // ── Query Fetch: Cancelled ──────────────────────────────────────

    [Fact]
    public async Task Fetch_RecordsCancelledMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var fetchCancelled = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_cancelled_total");
        var fetchDuration = new MetricCollector<double>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_duration");

        using var cts = new CancellationTokenSource();

        // Act — cancel while the query function is waiting
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.FetchQueryAsync(new FetchQueryOptions<string>
            {
                QueryKey = ["cancel"],
                QueryFn = async ctx =>
                {
                    cts.Cancel();
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    return "never";
                }
            }, cts.Token));

        // Assert — cancelled count recorded, but no duration
        Assert.Single(fetchCancelled.GetMeasurementSnapshot());
        Assert.Empty(fetchDuration.GetMeasurementSnapshot());
    }

    // ── Query Fetch: Deduplicated ───────────────────────────────────

    [Fact]
    public async Task Fetch_RecordsDeduplicatedMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var fetchTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_total");
        var fetchDedup = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.fetch_deduplicated_total");

        var tcs = new TaskCompletionSource<string>();

        // Build a query directly so we can call Fetch() twice
        var cache = client.GetQueryCache();
        var query = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["dedup"],
            GcTime = QueryTimeDefaults.GcTime
        });
        query.SetQueryFn(_ => tcs.Task);

        // Act — first fetch starts the in-flight task, second is deduplicated
        var fetch1 = query.Fetch();
        var fetch2 = query.Fetch();
        tcs.SetResult("done");
        await fetch1;
        await fetch2;

        // Assert — one real fetch, one deduplicated
        Assert.Single(fetchTotal.GetMeasurementSnapshot());
        Assert.Single(fetchDedup.GetMeasurementSnapshot());
    }

    // ── Cache Hit/Miss ──────────────────────────────────────────────

    [Fact]
    public async Task CacheHit_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var cacheHit = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.cache.hit_total");

        var options = new FetchQueryOptions<string>
        {
            QueryKey = ["hit-test"],
            QueryFn = async _ => "data",
            // StaleTime.MaxValue means data is always fresh after first fetch
            StaleTime = TimeSpan.MaxValue
        };

        // First fetch populates the cache
        await client.FetchQueryAsync(options);
        Assert.Empty(cacheHit.GetMeasurementSnapshot());

        // Act — second fetch should be a cache hit
        await client.FetchQueryAsync(options);

        // Assert
        Assert.Single(cacheHit.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task CacheMiss_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var cacheMiss = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.cache.miss_total");

        // Act — first fetch with StaleTime.Zero (default) is always a cache miss
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["miss-test"],
            QueryFn = async _ => "data"
        });

        // Assert
        Assert.Single(cacheMiss.GetMeasurementSnapshot());
    }

    // ── Cache Size ──────────────────────────────────────────────────

    [Fact]
    public void CacheSize_TracksAddAndRemove()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var cacheSize = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.cache.size");

        var cache = client.GetQueryCache();

        // Act — add two queries
        var q1 = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["size-a"],
            GcTime = QueryTimeDefaults.GcTime
        });
        var q2 = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["size-b"],
            GcTime = QueryTimeDefaults.GcTime
        });

        // Assert — two increments
        var snapshot = cacheSize.GetMeasurementSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, m => Assert.Equal(1, m.Value));

        // Act — remove one query
        cache.Remove(q1);

        // Assert — one decrement
        snapshot = cacheSize.GetMeasurementSnapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(-1, snapshot[2].Value);
    }

    // ── GC Removed ──────────────────────────────────────────────────

    [Fact]
    public void GcRemoved_RecordsMetrics()
    {
        // Arrange — use FakeTimeProvider to control GC timing
        var timeProvider = new FakeTimeProvider();
        var (client, meterFactory) = CreateClientWithMetrics(timeProvider);

        var gcRemoved = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.cache.gc_removed_total");

        var cache = client.GetQueryCache();
        cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["gc-test"],
            GcTime = TimeSpan.FromSeconds(10)
        });

        Assert.Single(cache.GetAll());

        // Act — advance past GcTime to trigger garbage collection
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Assert
        Assert.Empty(cache.GetAll());
        Assert.Single(gcRemoved.GetMeasurementSnapshot());
    }

    // ── Mutation: Success ───────────────────────────────────────────

    [Fact]
    public async Task Mutation_RecordsSuccessMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var mutationTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.total");
        var mutationSuccess = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.success_total");
        var mutationDuration = new MetricCollector<double>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.duration");

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            MutationKey = ["test-mutation"]
        };
        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("hello");

        // Assert
        var totals = mutationTotal.GetMeasurementSnapshot();
        Assert.Single(totals);

        var expectedHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["test-mutation"]);
        Assert.Equal(expectedHash, totals[0].Tags["rabstackquery.mutation.key"]);

        Assert.Single(mutationSuccess.GetMeasurementSnapshot());

        var durations = mutationDuration.GetMeasurementSnapshot();
        Assert.Single(durations);
        Assert.True(durations[0].Value >= 0);
    }

    // ── Mutation: Error ─────────────────────────────────────────────

    [Fact]
    public async Task Mutation_RecordsErrorMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var mutationTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.total");
        var mutationError = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.error_total");
        var mutationDuration = new MetricCollector<double>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.duration");

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (input, context, ct) =>
                throw new InvalidOperationException("mutation failed"),
            MutationKey = ["fail-mutation"]
        };
        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            observer.MutateAsync("hello"));

        // Assert
        Assert.Single(mutationTotal.GetMeasurementSnapshot());
        Assert.Single(mutationError.GetMeasurementSnapshot());

        var durations = mutationDuration.GetMeasurementSnapshot();
        Assert.Single(durations);
        Assert.True(durations[0].Value >= 0);
    }

    // ── Mutation Without Key: No Key Tag ────────────────────────────

    [Fact]
    public async Task MutationWithoutKey_OmitsKeyTag()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var mutationTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.mutation.total");

        // No MutationKey set — intentionally omitted
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };
        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert — measurement recorded but without a key tag
        var totals = mutationTotal.GetMeasurementSnapshot();
        Assert.Single(totals);
        Assert.False(totals[0].Tags.ContainsKey("rabstackquery.mutation.key"));
    }

    // ── Retry ───────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_RecordsMetrics()
    {
        // Arrange — build the query directly with a zero RetryDelay so retries
        // complete instantly. Using FakeTimeProvider here would deadlock because
        // FetchQueryAsync blocks before we can call Advance().
        var (client, meterFactory) = CreateClientWithMetrics();

        var retryTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.retry.total");

        var attempt = 0;
        var cache = client.GetQueryCache();
        var query = cache.Build<string, string>(client, new QueryConfiguration<string>
        {
            QueryKey = ["retry-test"],
            GcTime = QueryTimeDefaults.GcTime,
            Retry = 2,
            RetryDelay = (_, _) => TimeSpan.Zero
        });
        query.SetQueryFn(_ =>
        {
            attempt++;
            if (attempt < 3)
                throw new InvalidOperationException($"fail #{attempt}");
            return Task.FromResult("success");
        });

        // Act
        await query.Fetch();

        // Assert — 2 retry attempts (the initial attempt is not a retry)
        var retries = retryTotal.GetMeasurementSnapshot();
        Assert.Equal(2, retries.Count);
        Assert.All(retries, m =>
            Assert.Equal("query", m.Tags["rabstackquery.retry.source"]));
    }

    // ── Observer Active Count ───────────────────────────────────────

    [Fact]
    public void Observer_TracksActiveCount()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var activeObservers = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.observer.active");

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["observer-test"],
                QueryFn = async _ => "data",
                Enabled = false // Disable to prevent auto-fetch
            });

        // Act — subscribe activates the observer
        var sub = observer.Subscribe(_ => { });

        // Assert — increment
        var snapshot = activeObservers.GetMeasurementSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(1, snapshot[0].Value);

        // Act — unsubscribe deactivates the observer
        sub.Dispose();

        // Assert — decrement
        snapshot = activeObservers.GetMeasurementSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(-1, snapshot[1].Value);
    }

    // ── Invalidation ────────────────────────────────────────────────

    [Fact]
    public async Task Invalidation_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var invalidationTotal = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.invalidation_total");

        // Populate the cache so the query has state to invalidate
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["invalidate-test"],
            QueryFn = async _ => "data"
        });

        // Act
        await client.InvalidateQueries(["invalidate-test"]);

        // Assert
        Assert.Single(invalidationTotal.GetMeasurementSnapshot());
    }

    // ── Refetch on Focus ────────────────────────────────────────────

    [Fact]
    public async Task RefetchOnFocus_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var refetchOnFocus = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.refetch_on_focus_total");

        // Create an observer with default RefetchOnWindowFocus (WhenStale) and
        // StaleTime.Zero (always stale) so that OnFocus triggers a refetch.
        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["focus-test"],
                QueryFn = async _ => "data",
                RefetchOnWindowFocus = RefetchOnBehavior.Always
            });

        // Subscribe to activate the observer (required for OnFocus to see it)
        var sub = observer.Subscribe(_ => { });
        await Task.Delay(100); // Allow initial fetch to complete

        // Act — simulate focus regained
        client.GetQueryCache().OnFocus();
        await Task.Delay(100); // Allow fire-and-forget fetch to start

        // Assert
        Assert.Single(refetchOnFocus.GetMeasurementSnapshot());

        sub.Dispose();
    }

    // ── Refetch on Reconnect ────────────────────────────────────────

    [Fact]
    public async Task RefetchOnReconnect_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var refetchOnReconnect = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.query.refetch_on_reconnect_total");

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["reconnect-test"],
                QueryFn = async _ => "data",
                RefetchOnReconnect = RefetchOnBehavior.Always
            });

        var sub = observer.Subscribe(_ => { });
        await Task.Delay(100);

        // Act — simulate network reconnection
        client.GetQueryCache().OnOnline();
        await Task.Delay(100);

        // Assert
        Assert.Single(refetchOnReconnect.GetMeasurementSnapshot());

        sub.Dispose();
    }

    // ── No MeterFactory: No Exceptions ──────────────────────────────

    [Fact]
    public async Task NoMeterFactory_NoMetricsRecorded()
    {
        // Arrange — create client WITHOUT meterFactory
        var client = new QueryClient(new QueryCache());

        // Act — perform operations that would normally record metrics.
        // The key assertion is that no exception is thrown.
        await client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = ["no-metrics"],
            QueryFn = async _ => "data"
        });

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["no-metrics-observer"],
                QueryFn = async _ => "data",
                Enabled = false
            });

        var sub = observer.Subscribe(_ => { });
        sub.Dispose();

        await client.InvalidateQueries(["no-metrics"]);

        var mutationOptions = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };
        var mutationObserver = new MutationObserver<string, Exception, string, object?>(
            client, mutationOptions);
        await mutationObserver.MutateAsync("test");

        // Assert — if we got here, no NullReferenceException was thrown
    }

    // ── EnsureQueryDataAsync Cache Hit ──────────────────────────────

    [Fact]
    public async Task EnsureQueryDataAsync_CacheHit_RecordsMetrics()
    {
        // Arrange
        var (client, meterFactory) = CreateClientWithMetrics();

        var cacheHit = new MetricCollector<long>(
            meterFactory, QueryMetrics.MeterName, "rabstackquery.cache.hit_total");

        var options = new FetchQueryOptions<string>
        {
            QueryKey = ["ensure-test"],
            QueryFn = async _ => "data",
            StaleTime = TimeSpan.MaxValue
        };

        // Populate the cache
        await client.FetchQueryAsync(options);
        Assert.Empty(cacheHit.GetMeasurementSnapshot());

        // Act — EnsureQueryDataAsync should hit the cache
        await client.EnsureQueryDataAsync(options);

        // Assert
        Assert.Single(cacheHit.GetMeasurementSnapshot());
    }
}
