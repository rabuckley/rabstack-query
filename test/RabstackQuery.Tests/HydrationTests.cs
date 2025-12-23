using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery.Tests;

/// <summary>
/// Tests for cache hydration and dehydration. Ports test cases from TanStack's
/// hydration.test.tsx, adapted for C#'s type-erasure placeholder model.
/// </summary>
public sealed class HydrationTests
{
    private static QueryClient CreateQueryClient(TimeProvider? timeProvider = null)
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache, timeProvider: timeProvider);
    }

    // ── Core round-trip ───────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldWorkWithSerializableValues()
    {
        // Arrange — prefetch some queries
        var client = CreateQueryClient();
        client.SetQueryData(["string"], "hello");
        client.SetQueryData(["number"], 42);
        client.SetQueryData(["list"], new List<int> { 1, 2, 3 });

        // Act — dehydrate and hydrate into a new client
        var dehydrated = client.Dehydrate();
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Assert — data is discoverable via FindAll
        var queries = client2.GetQueryCache().GetAll().ToList();
        Assert.Equal(3, queries.Count);

        // Verify data round-tripped via placeholder state
        var stringQuery = queries.First(q => q.QueryHash == DefaultQueryKeyHasher.Instance.HashQueryKey(["string"]));
        Assert.Equal(QueryStatus.Succeeded, stringQuery.CurrentStatus);
    }

    [Fact]
    public void Dehydrate_ShouldWorkWithComplexKeys()
    {
        // Arrange
        var client = CreateQueryClient();
        QueryKey complexKey = ["users", new { Page = 1, Filter = "active" }];
        client.SetQueryData(complexKey, "page1");

        // Act
        var dehydrated = client.Dehydrate();
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Assert
        var queries = client2.GetQueryCache().GetAll().ToList();
        Assert.Single(queries);

        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(complexKey);
        Assert.Equal(queryHash, queries[0].QueryHash);
    }

    [Fact]
    public void Dehydrate_ShouldIncludeDehydratedAtTimestamp()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var client = CreateQueryClient(timeProvider);
        client.SetQueryData(["test"], "data");

        // Act
        var dehydrated = client.Dehydrate();

        // Assert
        Assert.Single(dehydrated.Queries);
        Assert.Equal(start.ToUnixTimeMilliseconds(), dehydrated.Queries[0].DehydratedAt);
    }

    [Fact]
    public void Dehydrate_ShouldIncludeQueryHashAndQueryKey()
    {
        // Arrange
        var client = CreateQueryClient();
        QueryKey key = ["todos", "active"];
        client.SetQueryData(key, "data");

        // Act
        var dehydrated = client.Dehydrate();

        // Assert
        var dq = Assert.Single(dehydrated.Queries);
        var expectedHash = DefaultQueryKeyHasher.Instance.HashQueryKey(key);
        Assert.Equal(expectedHash, dq.QueryHash);
        Assert.Equal(key, dq.QueryKey);
    }

    // ── Query filters ─────────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldOnlyDehydrateSuccessfulQueriesByDefault()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["success"], "ok");

        // Create a pending query (no data, no fetch)
        var pendingOptions = new QueryConfiguration<string>
        {
            QueryKey = ["pending"],
            GcTime = QueryTimeDefaults.GcTime,
        };
        client.GetQueryCache().Build<string, string>(client, pendingOptions);

        // Act
        var dehydrated = client.Dehydrate();

        // Assert — only the succeeded query is included
        Assert.Single(dehydrated.Queries);
        Assert.Equal(
            DefaultQueryKeyHasher.Instance.HashQueryKey(["success"]),
            dehydrated.Queries[0].QueryHash);
    }

    [Fact]
    public void Dehydrate_ShouldRespectCustomShouldDehydrateQuery()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["a"], "alpha");
        client.SetQueryData(["b"], "beta");

        // Act — only dehydrate queries whose hash starts with the hash for ["a"]
        var hashA = DefaultQueryKeyHasher.Instance.HashQueryKey(["a"]);
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = query => query.QueryHash == hashA,
        });

        // Assert
        Assert.Single(dehydrated.Queries);
        Assert.Equal(hashA, dehydrated.Queries[0].QueryHash);
    }

    [Fact]
    public void Hydrate_ShouldNotOverwriteNewerExistingQuery()
    {
        // Arrange — client1 has older data, client2 has newer data
        var client1 = CreateQueryClient();
        client1.SetQueryData(["key"], "old");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient();
        client2.SetQueryData(["key"], "new");

        // Act — hydrate older data into client2
        client2.Hydrate(dehydrated);

        // Assert — client2 should keep "new" since its DataUpdatedAt is newer
        var data = client2.GetQueryData<string>(["key"]);
        Assert.Equal("new", data);
    }

    [Fact]
    public void Hydrate_ShouldOverwriteOlderExistingQuery()
    {
        // Arrange — set up with controlled time so we can ensure ordering
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client1 = CreateQueryClient(time);
        time.Advance(TimeSpan.FromSeconds(10));
        client1.SetQueryData(["key"], "newer");
        var dehydrated = client1.Dehydrate();

        // client2 with older timestamp
        var time2 = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client2 = CreateQueryClient(time2);
        client2.SetQueryData(["key"], "older");

        // Act — hydrate newer data into client2
        client2.Hydrate(dehydrated);

        // Assert — should now have "newer"
        var data = client2.GetQueryData<string>(["key"]);
        Assert.Equal("newer", data);
    }

    [Fact]
    public void Hydrate_ShouldPreserveFetchStatus_ForExistingQueries()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client = CreateQueryClient(time);
        client.SetQueryData(["key"], "old");

        // Get the query and check its FetchStatus is Idle
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        var query = client.GetQueryCache().Get<string>(queryHash);
        Assert.NotNull(query);
        Assert.Equal(FetchStatus.Idle, query.CurrentFetchStatus);

        // Create newer dehydrated data
        time.Advance(TimeSpan.FromSeconds(10));
        var client2 = CreateQueryClient(time);
        client2.SetQueryData(["key"], "newer");
        var dehydrated = client2.Dehydrate();

        // Act — hydrate into original client
        client.Hydrate(dehydrated);

        // Assert — FetchStatus should be preserved (Idle)
        Assert.Equal(FetchStatus.Idle, query.CurrentFetchStatus);
    }

    [Fact]
    public void Hydrate_ShouldSetFetchStatusIdle_ForNewQueries()
    {
        // Arrange
        var client1 = CreateQueryClient();
        client1.SetQueryData(["key"], "data");
        var dehydrated = client1.Dehydrate();

        // Act — hydrate into empty client
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Assert
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        var query = client2.GetQueryCache().GetByHash(queryHash);
        Assert.NotNull(query);
        Assert.Equal(FetchStatus.Idle, query.CurrentFetchStatus);
    }

    // ── Options and defaults ──────────────────────────────────────────

    [Fact]
    public void Hydrate_ShouldUseGcTimeFromClient()
    {
        // Arrange
        var client1 = CreateQueryClient();
        client1.SetQueryData(["key"], "data");
        var dehydrated = client1.Dehydrate();

        // Act — hydrate with custom GcTime via HydrateOptions
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated, new HydrateOptions
        {
            Queries = new HydrateQueryDefaults { GcTime = TimeSpan.FromMinutes(10) }
        });

        // Assert — query exists in cache (specific GcTime cannot be inspected
        // externally, but the query being created is the key assertion)
        var queries = client2.GetQueryCache().GetAll().ToList();
        Assert.Single(queries);
    }

    [Fact]
    public void Hydrate_ShouldAcceptDefaultQueryOptions()
    {
        // Arrange
        var client1 = CreateQueryClient();
        client1.SetQueryData(["key"], "data");
        var dehydrated = client1.Dehydrate();

        // Act — hydrate with custom retry via HydrateOptions
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated, new HydrateOptions
        {
            Queries = new HydrateQueryDefaults { Retry = 5 }
        });

        // Assert — query was created
        var queries = client2.GetQueryCache().GetAll().ToList();
        Assert.Single(queries);
    }

    [Fact]
    public void Hydrate_ShouldRespectClientDefaultDehydrateOptions()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["a"], "alpha");
        client.SetQueryData(["b"], "beta");

        // Only dehydrate query "a" via default options
        var hashA = DefaultQueryKeyHasher.Instance.HashQueryKey(["a"]);
        client.SetDefaultOptions(new QueryClientDefaultOptions
        {
            Dehydrate = new DehydrateOptions
            {
                ShouldDehydrateQuery = q => q.QueryHash == hashA,
            }
        });

        // Act
        var dehydrated = client.Dehydrate();

        // Assert
        Assert.Single(dehydrated.Queries);
        Assert.Equal(hashA, dehydrated.Queries[0].QueryHash);
    }

    [Fact]
    public void Hydrate_ShouldHandleNullState()
    {
        // Act & Assert — should not throw
        var client = CreateQueryClient();
        client.Hydrate(null);

        Assert.Empty(client.GetQueryCache().GetAll());
    }

    [Fact]
    public void Hydrate_ShouldHandleEmptyLists()
    {
        // Act
        var client = CreateQueryClient();
        client.Hydrate(new DehydratedState
        {
            Queries = [],
            Mutations = [],
        });

        // Assert
        Assert.Empty(client.GetQueryCache().GetAll());
        Assert.Empty(client.GetMutationCache().GetAll());
    }

    // ── Metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldRoundTripQueryMeta()
    {
        // Arrange
        var client = CreateQueryClient();

        var meta = new QueryMeta(new Dictionary<string, object?>
        {
            ["source"] = "server",
            ["version"] = 42,
        });

        var options = new QueryConfiguration<string>
        {
            QueryKey = ["meta-test"],
            GcTime = QueryTimeDefaults.GcTime,
            Meta = meta,
        };

        var state = new QueryState<string>
        {
            Data = "with-meta",
            DataUpdateCount = 1,
            DataUpdatedAt = 1000,
            Status = QueryStatus.Succeeded,
            FetchStatus = FetchStatus.Idle,
        };

        client.GetQueryCache().Build<string, string>(client, options, state);

        // Act
        var dehydrated = client.Dehydrate();
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Assert — meta survives the round trip
        var dq = Assert.Single(dehydrated.Queries);
        Assert.NotNull(dq.Meta);
        Assert.Equal("server", dq.Meta["source"]);
        Assert.Equal(42, dq.Meta["version"]);

        // Hydrated placeholder also has the meta
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["meta-test"]);
        var hydratedQuery = client2.GetQueryCache().GetByHash(queryHash);
        Assert.NotNull(hydratedQuery);
        Assert.NotNull(hydratedQuery.Meta);
        Assert.Equal("server", hydratedQuery.Meta["source"]);
    }

    [Fact]
    public void Dehydrate_ShouldRoundTripMutationMeta()
    {
        // Arrange — create a paused mutation with meta
        var client = CreateQueryClient();

        var meta = new MutationMeta(new Dictionary<string, object?>
        {
            ["intent"] = "update-profile",
        });

        var mutationOptions = new MutationOptions<object, Exception, object, object?>
        {
            MutationKey = ["profile"],
            Meta = meta,
        };

        var mutationState = new MutationState<object, object, object?>
        {
            IsPaused = true,
            Status = MutationStatus.Pending,
        };

        client.GetMutationCache().Build(client, mutationOptions, mutationState);

        // Act
        var dehydrated = client.Dehydrate();

        // Assert
        var dm = Assert.Single(dehydrated.Mutations);
        Assert.NotNull(dm.Meta);
        Assert.Equal("update-profile", dm.Meta["intent"]);
    }

    // ── Mutations ─────────────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldOnlyDehydratePausedMutationsByDefault()
    {
        // Arrange
        var client = CreateQueryClient();

        // Create a paused mutation
        var pausedState = new MutationState<object, object, object?>
        {
            IsPaused = true,
            Status = MutationStatus.Pending,
        };
        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["paused"] },
            pausedState);

        // Create a completed mutation (not paused)
        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["done"] });

        // Act
        var dehydrated = client.Dehydrate();

        // Assert — only the paused mutation
        Assert.Single(dehydrated.Mutations);
    }

    [Fact]
    public void Dehydrate_ShouldRespectCustomShouldDehydrateMutation()
    {
        // Arrange
        var client = CreateQueryClient();

        // Create two mutations — neither paused
        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["a"] });
        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["b"] });

        // Act — dehydrate all mutations regardless of paused status
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateMutation = _ => true,
        });

        // Assert
        Assert.Equal(2, dehydrated.Mutations.Count);
    }

    [Fact]
    public void Dehydrate_ShouldNotDehydrateNonPausedMutations()
    {
        // Arrange
        var client = CreateQueryClient();

        // Create a non-paused mutation
        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["active"] });

        // Act
        var dehydrated = client.Dehydrate();

        // Assert
        Assert.Empty(dehydrated.Mutations);
    }

    [Fact]
    public void Dehydrate_ShouldRoundTripMutationScopes()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("order-updates");

        var mutationState = new MutationState<object, object, object?>
        {
            IsPaused = true,
            Status = MutationStatus.Pending,
        };

        client.GetMutationCache().Build(client,
            new MutationOptions<object, Exception, object, object?>
            {
                MutationKey = ["order"],
                Scope = scope,
            },
            mutationState);

        // Act
        var dehydrated = client.Dehydrate();
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Assert
        var dm = Assert.Single(dehydrated.Mutations);
        Assert.NotNull(dm.Scope);
        Assert.Equal("order-updates", dm.Scope.Id);
    }

    [Fact]
    public void Hydrate_ShouldAcceptDefaultMutationOptions()
    {
        // Arrange
        var client1 = CreateQueryClient();
        var mutationState = new MutationState<object, object, object?>
        {
            IsPaused = true,
            Status = MutationStatus.Pending,
        };
        client1.GetMutationCache().Build(client1,
            new MutationOptions<object, Exception, object, object?> { MutationKey = ["test"] },
            mutationState);

        var dehydrated = client1.Dehydrate();

        // Act
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated, new HydrateOptions
        {
            Mutations = new HydrateMutationDefaults
            {
                GcTime = TimeSpan.FromMinutes(20),
            }
        });

        // Assert — mutation was created
        var mutations = client2.GetMutationCache().GetAll().ToList();
        Assert.Single(mutations);
    }

    // ── C#-specific: placeholder lifecycle ────────────────────────────

    [Fact]
    public void Hydrate_StagedQueryUpgradedOnBuild()
    {
        // Arrange — hydrate a string value, then Build<string> for the same hash
        var client1 = CreateQueryClient();
        client1.SetQueryData(["items"], "hydrated-data");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Before upgrade: query exists as placeholder
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["items"]);
        var placeholder = client2.GetQueryCache().GetByHash(queryHash);
        Assert.NotNull(placeholder);
        Assert.True(placeholder.IsHydratedPlaceholder);

        // Act — Build triggers upgrade from Query<object> to Query<string>
        var options = new QueryConfiguration<string>
        {
            QueryKey = ["items"],
            GcTime = QueryTimeDefaults.GcTime,
        };
        var typedQuery = client2.GetQueryCache().Build<string, string>(client2, options);

        // Assert
        Assert.False(typedQuery.IsHydratedPlaceholder);
        Assert.Equal("hydrated-data", typedQuery.State?.Data);
        Assert.Equal(QueryStatus.Succeeded, typedQuery.CurrentStatus);
    }

    [Fact]
    public void Hydrate_PlaceholderDiscoverableViaFindAll()
    {
        // Arrange
        var client1 = CreateQueryClient();
        client1.SetQueryData(["a"], "alpha");
        client1.SetQueryData(["b"], "beta");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Act
        var all = client2.GetQueryCache().GetAll().ToList();
        var findAll = client2.GetQueryCache().FindAll().ToList();

        // Assert — hydrated placeholders are visible
        Assert.Equal(2, all.Count);
        Assert.Equal(2, findAll.Count);
    }

    [Fact]
    public void Hydrate_PlaceholderGarbageCollected()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client1 = CreateQueryClient(time);
        client1.SetQueryData(["gc-test"], "data");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient(time);
        client2.Hydrate(dehydrated);

        // Verify placeholder exists
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["gc-test"]);
        Assert.NotNull(client2.GetQueryCache().GetByHash(queryHash));

        // Act — advance past GC time (default 5 minutes)
        time.Advance(TimeSpan.FromMinutes(6));

        // Assert — placeholder should be garbage collected
        Assert.Null(client2.GetQueryCache().GetByHash(queryHash));
    }

    [Fact]
    public void Hydrate_GetQueryDataReturnsDefault_BeforeUpgrade()
    {
        // Arrange — hydrate a string value
        var client1 = CreateQueryClient();
        client1.SetQueryData(["typed"], "before-upgrade");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Act — GetQueryData<string> calls Get<string> which returns null for
        // the Query<object> placeholder (type mismatch). This is a deliberate
        // C# divergence from TanStack where getQueryData always works because
        // JavaScript has no runtime type checking.
        var result = client2.GetQueryData<string>(["typed"]);

        // Assert — returns default because placeholder hasn't been upgraded yet
        Assert.Null(result);
    }

    [Fact]
    public void Hydrate_SetQueryDataUpgradesPlaceholder()
    {
        // Arrange — hydrate, then SetQueryData<string> for the same key
        var client1 = CreateQueryClient();
        client1.SetQueryData(["upgrade"], "original");
        var dehydrated = client1.Dehydrate();

        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated);

        // Act — SetQueryData goes through Build which upgrades the placeholder
        client2.SetQueryData(["upgrade"], "updated");

        // Assert
        var data = client2.GetQueryData<string>(["upgrade"]);
        Assert.Equal("updated", data);

        // Verify it's no longer a placeholder
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["upgrade"]);
        var query = client2.GetQueryCache().GetByHash(queryHash);
        Assert.NotNull(query);
        Assert.False(query.IsHydratedPlaceholder);
    }

    // ── Data transformers ────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldApplySerializeData()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["date"], new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero));

        // Act — serialize DateTimeOffset to ISO string
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            SerializeData = data => data is DateTimeOffset dto ? dto.ToString("O") : data,
        });

        // Assert
        var dq = Assert.Single(dehydrated.Queries);
        Assert.Equal("2026-03-13T00:00:00.0000000+00:00", dq.State.Data);
    }

    [Fact]
    public void Hydrate_ShouldApplyDeserializeData()
    {
        // Arrange — dehydrate with serialize, hydrate with deserialize
        var client1 = CreateQueryClient();
        client1.SetQueryData(["date"], new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero));

        var dehydrated = client1.Dehydrate(new DehydrateOptions
        {
            SerializeData = data => data is DateTimeOffset dto ? dto.ToString("O") : data,
        });

        // Act
        var client2 = CreateQueryClient();
        client2.Hydrate(dehydrated, new HydrateOptions
        {
            DeserializeData = data => data is string s && DateTimeOffset.TryParse(s, out var dto) ? dto : data,
        });

        // Assert — upgrade the placeholder and verify data was deserialized
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["date"]);
        var deserialized = client2.GetQueryCache().TryGetHydratedData<DateTimeOffset>(queryHash);
        Assert.Equal(new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero), deserialized);
    }

    [Fact]
    public void Hydrate_ShouldApplyDeserializeData_WhenOverwritingExistingQuery()
    {
        // Arrange — hydrate into a client that already has a query for the same key
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client1 = CreateQueryClient(time);
        time.Advance(TimeSpan.FromSeconds(10));
        client1.SetQueryData(["key"], 100);

        var dehydrated = client1.Dehydrate(new DehydrateOptions
        {
            SerializeData = data => data is int n ? n.ToString() : data,
        });

        // Client2 has older data
        var time2 = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var client2 = CreateQueryClient(time2);
        client2.SetQueryData(["key"], 0);

        // Act
        client2.Hydrate(dehydrated, new HydrateOptions
        {
            DeserializeData = data => data is string s && int.TryParse(s, out var n) ? n : data,
        });

        // Assert — data was deserialized and applied to the existing Query<int>
        Assert.Equal(100, client2.GetQueryData<int>(["key"]));
    }

    [Fact]
    public void Dehydrate_ShouldNotCallSerializeData_WhenDataIsNull()
    {
        // Arrange — create a query with no data (pending)
        var client = CreateQueryClient();
        var options = new QueryConfiguration<string>
        {
            QueryKey = ["pending"],
            GcTime = QueryTimeDefaults.GcTime,
        };
        client.GetQueryCache().Build<string, string>(client, options);

        var callCount = 0;

        // Act — dehydrate all queries (including pending)
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
            SerializeData = data => { callCount++; return data; },
        });

        // Assert — transform was never called because data was null
        Assert.Single(dehydrated.Queries);
        Assert.Null(dehydrated.Queries[0].State.Data);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public void Dehydrate_SerializeData_ThatReturnsNull()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetQueryData(["key"], "value");

        // Act — serialize returns null
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            SerializeData = _ => null,
        });

        // Assert — data is null after serialization
        var dq = Assert.Single(dehydrated.Queries);
        Assert.Null(dq.State.Data);
    }

    // ── Error redaction ───────────────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldRedactErrors_ByDefault()
    {
        // Arrange — create a query in error state
        var client = CreateQueryClient();
        var state = new QueryState<string>
        {
            Error = new InvalidOperationException("secret connection string leaked"),
            ErrorUpdateCount = 1,
            ErrorUpdatedAt = 1000,
            Status = QueryStatus.Errored,
            FetchStatus = FetchStatus.Idle,
        };
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["errored"], GcTime = QueryTimeDefaults.GcTime },
            state);

        // Act — default ShouldRedactErrors is _ => true
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
        });

        // Assert — error is redacted
        var dq = Assert.Single(dehydrated.Queries);
        Assert.NotNull(dq.State.Error);
        Assert.Equal("redacted", dq.State.Error.Message);
        Assert.IsType<Exception>(dq.State.Error);
    }

    [Fact]
    public void Dehydrate_ShouldPreserveErrors_WhenRedactionDisabled()
    {
        // Arrange
        var client = CreateQueryClient();
        var originalError = new InvalidOperationException("keep me");
        var state = new QueryState<string>
        {
            Error = originalError,
            ErrorUpdateCount = 1,
            ErrorUpdatedAt = 1000,
            Status = QueryStatus.Errored,
            FetchStatus = FetchStatus.Idle,
        };
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["errored"], GcTime = QueryTimeDefaults.GcTime },
            state);

        // Act — disable redaction
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
            ShouldRedactErrors = _ => false,
        });

        // Assert — original error preserved
        var dq = Assert.Single(dehydrated.Queries);
        Assert.Same(originalError, dq.State.Error);
    }

    [Fact]
    public void Dehydrate_ShouldRedactErrors_Selectively()
    {
        // Arrange — two queries with different error types
        var client = CreateQueryClient();

        var sensitiveError = new InvalidOperationException("SQL connection: server=prod;password=s3cret");
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["sensitive"], GcTime = QueryTimeDefaults.GcTime },
            new QueryState<string>
            {
                Error = sensitiveError,
                ErrorUpdateCount = 1,
                ErrorUpdatedAt = 1000,
                Status = QueryStatus.Errored,
                FetchStatus = FetchStatus.Idle,
            });

        var safeError = new ArgumentException("Invalid page number");
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["safe"], GcTime = QueryTimeDefaults.GcTime },
            new QueryState<string>
            {
                Error = safeError,
                ErrorUpdateCount = 1,
                ErrorUpdatedAt = 1000,
                Status = QueryStatus.Errored,
                FetchStatus = FetchStatus.Idle,
            });

        // Act — only redact InvalidOperationException
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
            ShouldRedactErrors = ex => ex is InvalidOperationException,
        });

        // Assert
        Assert.Equal(2, dehydrated.Queries.Count);

        var sensitiveHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["sensitive"]);
        var safeHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["safe"]);

        var sensitiveQuery = dehydrated.Queries.First(q => q.QueryHash == sensitiveHash);
        var safeQuery = dehydrated.Queries.First(q => q.QueryHash == safeHash);

        Assert.Equal("redacted", sensitiveQuery.State.Error!.Message);
        Assert.Same(safeError, safeQuery.State.Error);
    }

    [Fact]
    public void Dehydrate_ShouldRedactFetchFailureReason()
    {
        // Arrange — query with both Error and FetchFailureReason
        var client = CreateQueryClient();
        var failureReason = new InvalidOperationException("connection string exposed");
        var state = new QueryState<string>
        {
            Data = "stale-data",
            DataUpdateCount = 1,
            DataUpdatedAt = 500,
            Error = new InvalidOperationException("also sensitive"),
            ErrorUpdateCount = 1,
            ErrorUpdatedAt = 1000,
            FetchFailureCount = 3,
            FetchFailureReason = failureReason,
            Status = QueryStatus.Errored,
            FetchStatus = FetchStatus.Idle,
        };
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["failing"], GcTime = QueryTimeDefaults.GcTime },
            state);

        // Act
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
        });

        // Assert — both error fields are redacted
        var dq = Assert.Single(dehydrated.Queries);
        Assert.Equal("redacted", dq.State.Error!.Message);
        Assert.Equal("redacted", dq.State.FetchFailureReason!.Message);
    }

    // ── Per-property resolution ───────────────────────────────────────

    [Fact]
    public void Dehydrate_ShouldResolveOptionsPerProperty()
    {
        // Arrange — client defaults provide ShouldRedactErrors,
        // parameter provides ShouldDehydrateQuery. Both should compose.
        var client = CreateQueryClient();
        client.SetQueryData(["ok"], "data");

        var originalError = new InvalidOperationException("keep me");
        client.GetQueryCache().Build<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["err"], GcTime = QueryTimeDefaults.GcTime },
            new QueryState<string>
            {
                Error = originalError,
                ErrorUpdateCount = 1,
                ErrorUpdatedAt = 1000,
                Status = QueryStatus.Errored,
                FetchStatus = FetchStatus.Idle,
            });

        // Client default: don't redact errors
        client.SetDefaultOptions(new QueryClientDefaultOptions
        {
            Dehydrate = new DehydrateOptions
            {
                ShouldRedactErrors = _ => false,
            },
        });

        // Act — parameter overrides ShouldDehydrateQuery but inherits ShouldRedactErrors
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateQuery = _ => true,
        });

        // Assert — both queries included (from parameter), errors preserved (from client default)
        Assert.Equal(2, dehydrated.Queries.Count);
        var errHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["err"]);
        var errQuery = dehydrated.Queries.First(q => q.QueryHash == errHash);
        Assert.Same(originalError, errQuery.State.Error);
    }

    [Fact]
    public void Hydrate_ShouldResolveDeserializeData_FromClientDefaults()
    {
        // Arrange
        var client1 = CreateQueryClient();
        client1.SetQueryData(["key"], 42);

        var dehydrated = client1.Dehydrate(new DehydrateOptions
        {
            SerializeData = data => data is int n ? n.ToString() : data,
        });

        // Act — client2 has DeserializeData in client defaults
        var client2 = CreateQueryClient();
        client2.SetDefaultOptions(new QueryClientDefaultOptions
        {
            Hydrate = new HydrateOptions
            {
                DeserializeData = data => data is string s && int.TryParse(s, out var n) ? n : data,
            },
        });
        client2.Hydrate(dehydrated);

        // Assert — placeholder data was deserialized back to int
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["key"]);
        Assert.Equal(42, client2.GetQueryCache().TryGetHydratedData<int>(queryHash));
    }

    // ── Helper ────────────────────────────────────────────────────────

    private static QueryClient CreateQueryClient(FakeTimeProvider timeProvider)
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache, timeProvider: timeProvider);
    }
}
