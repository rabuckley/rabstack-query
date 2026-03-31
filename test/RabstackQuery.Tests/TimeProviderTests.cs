using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

/// <summary>
/// Deterministic time tests using <see cref="FakeTimeProvider"/> to verify that
/// GC timers, staleness checks, retry backoff, timestamps, and refetch intervals
/// all route through the injected <see cref="TimeProvider"/> rather than
/// <c>DateTimeOffset.UtcNow</c> or <c>new Timer(...)</c>.
/// </summary>
public sealed class TimeProviderTests
{
    private static QueryClient CreateQueryClient(FakeTimeProvider timeProvider)
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache, timeProvider: timeProvider);
    }

    private static Query<TData> BuildQuery<TData>(
        QueryClient client,
        QueryKey queryKey,
        TimeSpan? gcTime = null,
        int retry = 3)
    {
        var cache = client.QueryCache;
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        var options = new QueryConfiguration<TData>
        {
            QueryKey = queryKey,
            QueryHash = queryHash,
            GcTime = gcTime ?? QueryTimeDefaults.GcTime,
            Retry = retry,
        };
        return cache.GetOrCreate<TData, TData>(client, options);
    }

    // ── GC Timer ──────────────────────────────────────────────────────

    [Fact]
    public void GcTimer_Should_Remove_Query_When_Time_Advances_Past_GcTime()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);
        var cache = client.QueryCache;
        var query = BuildQuery<string>(client, ["gc-test"], gcTime: TimeSpan.FromMilliseconds(10_000));

        Assert.Single(cache.GetAll());

        // Act — advance just under the GC time; query should still be in cache
        timeProvider.Advance(TimeSpan.FromMilliseconds(9_999));
        Assert.Single(cache.GetAll());

        // Act — advance past the GC time; query should be removed
        timeProvider.Advance(TimeSpan.FromMilliseconds(2));

        // Assert
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void GcTimer_Should_Not_Remove_Query_With_Active_Observers()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);
        var cache = client.QueryCache;
        var query = BuildQuery<string>(client, ["gc-observer"], gcTime: TimeSpan.FromMilliseconds(1_000));

        // Add an observer to keep the query alive
        query.SetQueryFn(_ => Task.FromResult("data"));
        var observer = new QueryObserver<string, string>(client, new QueryObserverOptions<string, string>
        {
            QueryKey = ["gc-observer"],
            Enabled = true,
            QueryFn = _ => Task.FromResult("data")
        });
        // Subscribe to keep the observer active
        observer.Subscribe(_ => { });

        // Act — advance well past GC time
        timeProvider.Advance(TimeSpan.FromMilliseconds(5_000));

        // Assert — query should NOT be removed because it has an active observer
        Assert.Single(cache.GetAll());
    }

    // ── Staleness ─────────────────────────────────────────────────────

    [Fact]
    public async Task IsStale_Should_Transition_When_Time_Advances_Past_StaleTime()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);

        var observerOptions = new QueryObserverOptions<string, string>
        {
            QueryKey = ["stale-test"],
            StaleTime = TimeSpan.FromMilliseconds(5_000),
            Enabled = true,
            QueryFn = _ => Task.FromResult("fresh-data")
        };
        var observer = new QueryObserver<string, string>(client, observerOptions);

        // Subscribe to trigger the initial fetch
        var fetchComplete = new TaskCompletionSource();
        observer.Subscribe(result =>
        {
            if (result.IsSuccess)
                fetchComplete.TrySetResult();
        });
        await fetchComplete.Task;

        // Assert — data is fresh immediately after fetch
        var result = observer.CurrentResult;
        Assert.True(result.IsSuccess);
        Assert.False(result.IsStale);

        // Act — advance time just under StaleTime
        timeProvider.Advance(TimeSpan.FromMilliseconds(4_999));
        result = observer.CurrentResult;
        Assert.False(result.IsStale);

        // Act — advance to exactly StaleTime boundary (>= means stale at 5000ms)
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        result = observer.CurrentResult;

        // Assert — data is now stale at the exact boundary
        Assert.True(result.IsStale);
    }

    [Fact]
    public void IsDataStale_Should_Use_TimeProvider_Clock()
    {
        // Arrange — start the clock at a known time
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        // Set some data — DataUpdatedAt will use the fake clock
        client.SetQueryData<string>(["stale-clock"], "initial");

        // The data was set at `start`. FetchQuery with StaleTime=10_000
        // should consider it fresh.
        var cache = client.QueryCache;
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["stale-clock"]);
        var query = cache.Get<string>(hash);
        Assert.NotNull(query);
        Assert.Equal(start.ToUnixTimeMilliseconds(), query!.State!.DataUpdatedAt);

        // Advance time past the stale threshold
        timeProvider.Advance(TimeSpan.FromMilliseconds(10_001));

        // Now EnsureQueryData should consider the data stale (staleTime=10_000)
        // and require a fresh fetch. We verify this indirectly: the observer
        // ShouldFetchOnMount check uses TimeProvider too.
        var observerOptions = new QueryObserverOptions<string, string>
        {
            QueryKey = ["stale-clock"],
            StaleTime = TimeSpan.FromMilliseconds(10_000),
            Enabled = true,
            QueryFn = _ => Task.FromResult("refreshed")
        };
        var observer = new QueryObserver<string, string>(client, observerOptions);
        var result = observer.CurrentResult;
        Assert.True(result.IsStale);
    }

    // ── Timestamps ────────────────────────────────────────────────────

    [Fact]
    public async Task DataUpdatedAt_Should_Use_Fake_Clock()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        var query = BuildQuery<string>(client, ["timestamp-test"]);
        query.SetQueryFn(_ => Task.FromResult("result"));

        // Act — advance 1 second then fetch
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await query.Fetch();

        // Assert — DataUpdatedAt should reflect the fake clock, not real wall time
        var expectedMs = start.AddSeconds(1).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, query.State!.DataUpdatedAt);
    }

    [Fact]
    public async Task ErrorUpdatedAt_Should_Use_Fake_Clock()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        var query = BuildQuery<string>(client, ["error-timestamp"], retry: 0);
        query.SetQueryFn(_ => throw new InvalidOperationException("boom"));

        // Act — advance 2 seconds then fetch (which will fail)
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => query.Fetch());

        // Assert — ErrorUpdatedAt should reflect the fake clock
        var expectedMs = start.AddSeconds(2).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, query.State!.ErrorUpdatedAt);
    }

    [Fact]
    public void SetQueryData_Should_Use_Fake_Clock_For_DataUpdatedAt()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        // Act — advance then set data
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        client.SetQueryData<string>(["manual-set"], "manual-data");

        // Assert
        var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["manual-set"]);
        var query = client.QueryCache.Get<string>(hash);
        var expectedMs = start.AddMinutes(5).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, query!.State!.DataUpdatedAt);
    }

    [Fact]
    public async Task Mutation_SubmittedAt_Should_Use_Fake_Clock()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        var mutationCache = client.MutationCache;
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult($"result-{vars}")
        };
        var mutation = mutationCache.GetOrCreate(client, options);

        // Act — advance 30 seconds then execute
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await mutation.Execute("input");

        // Assert
        var expectedMs = start.AddSeconds(30).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, mutation.State.SubmittedAt);
    }

    // ── Retry Backoff ─────────────────────────────────────────────────

    [Fact(Timeout = 10_000)]
    public async Task Retry_Backoff_Should_Advance_With_FakeTimeProvider()
    {
        // Arrange — single retry is sufficient to prove the delay timer routes
        // through TimeProvider. Multi-retry continuation paths are covered by
        // RetryerTests (which use TimeSpan.Zero + TimeProvider.System).
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);

        var callCount = 0;
        var query = BuildQuery<string>(client, ["retry-test"], retry: 1);
        query.Options.RetryDelay = (_, _) => TimeSpan.FromMilliseconds(1);

        query.SetQueryFn(_ =>
        {
            callCount++;
            if (callCount < 2)
                throw new InvalidOperationException($"fail #{callCount}");
            return Task.FromResult("success");
        });

        // Act — Fetch runs synchronously through: attempt 1 → throw → catch →
        // RetryDelay → DelayAsync → CreateTimer → await. The timer exists when
        // Fetch returns — no cross-thread synchronization needed.
        var fetchTask = query.Fetch();
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await fetchTask;

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(QueryStatus.Succeeded, query.State!.Status);
        Assert.Equal("success", query.State.Data);
    }

    // ── Refetch Interval ──────────────────────────────────────────────

    [Fact]
    public async Task RefetchInterval_Should_Fire_When_FakeTime_Advances()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);

        var fetchCount = 0;
        var refetchSignal = new TaskCompletionSource();
        var observerOptions = new QueryObserverOptions<string, string>
        {
            QueryKey = ["interval-test"],
            Enabled = true,
            RefetchInterval = TimeSpan.FromMilliseconds(5_000),
            RefetchIntervalInBackground = true, // Don't depend on FocusManager
            QueryFn = _ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                // Signal when a refetch (beyond the initial fetch) completes
                if (count > 1)
                    refetchSignal.TrySetResult();
                return Task.FromResult($"data-{count}");
            }
        };
        var observer = new QueryObserver<string, string>(client, observerOptions);

        // Subscribe to activate polling
        var fetchComplete = new TaskCompletionSource();
        observer.Subscribe(result =>
        {
            if (result.IsSuccess)
                fetchComplete.TrySetResult();
        });
        await fetchComplete.Task;

        var initialCount = fetchCount;

        // Act — advance one interval period
        timeProvider.Advance(TimeSpan.FromMilliseconds(5_000));

        // Wait for the fire-and-forget refetch to complete via signal
        await refetchSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — at least one additional fetch should have occurred
        Assert.True(fetchCount > initialCount,
            $"Expected more than {initialCount} fetches, got {fetchCount}");
    }

    // ── Initial Data Timestamp ────────────────────────────────────────

    [Fact]
    public void InitialData_Should_Use_Fake_Clock_For_DataUpdatedAt()
    {
        // Arrange — start the clock at a known time
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        // Advance 10 seconds before creating query with initial data
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var cache = client.QueryCache;
        var options = new QueryConfiguration<string>
        {
            QueryKey = ["initial-data"],
            GcTime = QueryTimeDefaults.GcTime,
            InitialData = "seeded"
        };
        var query = cache.GetOrCreate<string, string>(client, options);

        // Assert — DataUpdatedAt should use the fake clock
        var expectedMs = start.AddSeconds(10).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, query.State!.DataUpdatedAt);
        Assert.Equal("seeded", query.State.Data);
    }

    // ── IsStale with epoch-zero clock ─────────────────────────────────

    [Fact]
    public void IsStale_Should_Return_True_When_Never_Fetched_And_Clock_At_Epoch()
    {
        // Arrange — FakeTimeProvider default constructor starts at epoch (0),
        // which previously caused DataUpdatedAt=0 and elapsed=0, making
        // IsStale incorrectly return false.
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);

        var observerOptions = new QueryObserverOptions<string, string>
        {
            QueryKey = ["epoch-stale"],
            StaleTime = TimeSpan.FromMilliseconds(5_000),
            Enabled = true, // No Subscribe() call, so no auto-fetch happens
            QueryFn = _ => Task.FromResult("data")
        };
        var observer = new QueryObserver<string, string>(client, observerOptions);

        // Act
        var result = observer.CurrentResult;

        // Assert — never-fetched data should always be stale
        Assert.True(result.IsStale);
    }

    // ── Reset() timestamp ─────────────────────────────────────────────

    [Fact]
    public void Reset_Should_Use_Current_Clock_For_InitialData_Timestamp()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        var cache = client.QueryCache;
        var options = new QueryConfiguration<string>
        {
            QueryKey = ["reset-timestamp"],
            GcTime = QueryTimeDefaults.GcTime,
            InitialData = "original"
        };
        var query = cache.GetOrCreate<string, string>(client, options);

        // Verify initial timestamp is at `start`
        Assert.Equal(start.ToUnixTimeMilliseconds(), query.State!.DataUpdatedAt);

        // Advance clock then reset — the new initial state should use the
        // advanced clock, not the original construction time.
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        query.Reset();

        var expectedMs = start.AddMinutes(10).ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, query.State!.DataUpdatedAt);
        Assert.Equal("original", query.State.Data);
    }

    // ── QueryResult.IsStale with IsInvalidated ────────────────────────

    [Fact]
    public async Task IsStale_Should_Return_True_When_Invalidated_Even_If_StaleTime_Not_Elapsed()
    {
        // Arrange — fetch data so the query is fresh, then invalidate it.
        // Tests QueryResult.IsStale's isInvalidated check, mirroring TanStack's
        // isStaleByTime (query.ts:318): `if (this.state.isInvalidated) return true`.
        var timeProvider = new FakeTimeProvider();
        var client = CreateQueryClient(timeProvider);

        var observerOptions = new QueryObserverOptions<string, string>
        {
            QueryKey = ["invalidated-stale"],
            StaleTime = TimeSpan.FromHours(1),
            Enabled = true,
            QueryFn = _ => Task.FromResult("data")
        };
        var observer = new QueryObserver<string, string>(client, observerOptions);

        var fetchComplete = new TaskCompletionSource();
        observer.Subscribe(result =>
        {
            if (result.IsSuccess)
                fetchComplete.TrySetResult();
        });
        await fetchComplete.Task;

        // Assert — data should be fresh (1-hour stale time, no time elapsed)
        var result = observer.CurrentResult;
        Assert.False(result.IsStale);

        // Act — call Invalidate() directly on the query, mirroring the first
        // half of TanStack's invalidateQueries (queryClient.ts:298-300).
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["invalidated-stale"]);
        var query = client.QueryCache.Get<string>(queryHash)!;
        query.Invalidate();

        // Assert — QueryResult.IsStale should now report true despite stale
        // time not having elapsed, because the query is invalidated.
        result = observer.CurrentResult;
        Assert.True(result.IsStale);
    }

    // ── IsDataStale with IsInvalidated ────────────────────────────────

    [Fact]
    public async Task FetchQueryAsync_Should_Refetch_When_Invalidated_Even_If_StaleTime_Not_Elapsed()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);

        var fetchCount = 0;
        var fetchOptions = new FetchQueryOptions<string>
        {
            QueryKey = ["invalidated-fetch"],
            StaleTime = TimeSpan.FromSeconds(60), // 60 seconds — data should be fresh
            QueryFn = _ =>
            {
                fetchCount++;
                return Task.FromResult($"data-{fetchCount}");
            }
        };

        // First fetch to seed the cache
        var result1 = await client.FetchQueryAsync(fetchOptions);
        Assert.Equal("data-1", result1);
        Assert.Equal(1, fetchCount);

        // Calling again immediately should return cached data (still fresh)
        var result2 = await client.FetchQueryAsync(fetchOptions);
        Assert.Equal("data-1", result2);
        Assert.Equal(1, fetchCount);

        // Invalidate the query
        await client.InvalidateQueriesAsync(["invalidated-fetch"]);

        // Despite StaleTime not elapsed, FetchQueryAsync should refetch
        // because the query is invalidated
        var result3 = await client.FetchQueryAsync(fetchOptions);
        Assert.Equal("data-2", result3);
        Assert.Equal(2, fetchCount);
    }
}
