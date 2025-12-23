namespace RabstackQuery.Tests;

/// <summary>
/// Tests for QueryCache: storage operations, notification events, and focus/online refetch triggers.
/// </summary>
public sealed class QueryCacheTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    private static Query<TData> BuildQuery<TData>(QueryClient client, QueryKey queryKey, QueryState<TData>? state = null)
    {
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(queryKey);
        var options = new QueryConfiguration<TData> { QueryKey = queryKey, QueryHash = queryHash, GcTime = QueryTimeDefaults.GcTime };
        return cache.Build<TData, TData>(client, options, state);
    }

    #region Get / GetAll / GetByHash

    [Fact]
    public void Get_Should_Return_Query_By_Hash()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Act
        var found = client.GetQueryCache().Get<string>(query.QueryHash!);

        // Assert
        Assert.NotNull(found);
        Assert.Same(query, found);
    }

    [Fact]
    public void Get_Should_Return_Null_For_Unknown_Hash()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var found = client.GetQueryCache().Get<string>("nonexistent-hash");

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public void Get_Should_Throw_When_Type_Mismatches()
    {
        // Arrange
        var client = CreateQueryClient();
        BuildQuery<string>(client, ["todos"]);
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);

        // Act & Assert — requesting the wrong TData should throw
        Assert.Throws<InvalidOperationException>(() => client.GetQueryCache().Get<int>(queryHash));
    }

    [Fact]
    public void GetAll_Should_Return_All_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        BuildQuery<string>(client, ["todos"]);
        BuildQuery<int>(client, ["count"]);

        // Act
        var all = client.GetQueryCache().GetAll().ToList();

        // Assert
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetByHash_Should_Return_Query_Without_Type_Cast()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);

        // Act
        var found = client.GetQueryCache().GetByHash(query.QueryHash!);

        // Assert
        Assert.NotNull(found);
        Assert.Same(query, found);
    }

    [Fact]
    public void GetByHash_Should_Return_Null_For_Unknown_Hash()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var found = client.GetQueryCache().GetByHash("nonexistent");

        // Assert
        Assert.Null(found);
    }

    #endregion

    #region Build

    [Fact]
    public void Build_Should_Reuse_Existing_Query_For_Same_Key()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var queryHash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
        var options = new QueryConfiguration<string> { QueryKey = ["todos"], QueryHash = queryHash, GcTime = QueryTimeDefaults.GcTime };

        // Act
        var query1 = cache.Build<string, string>(client, options);
        var query2 = cache.Build<string, string>(client, options);

        // Assert
        Assert.Same(query1, query2);
    }

    [Fact]
    public void Build_Should_Create_Different_Queries_For_Different_Keys()
    {
        // Arrange
        var client = CreateQueryClient();

        // Act
        var query1 = BuildQuery<string>(client, ["todos"]);
        var query2 = BuildQuery<string>(client, ["posts"]);

        // Assert
        Assert.NotSame(query1, query2);
        Assert.NotEqual(query1.QueryHash, query2.QueryHash);
    }

    [Fact]
    public void Build_Should_Throw_When_QueryKey_Is_Null()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        var options = new QueryConfiguration<string> { QueryKey = null, GcTime = QueryTimeDefaults.GcTime };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cache.Build<string, string>(client, options));
    }

    /// <summary>
    /// TanStack: "should compute queryHash from queryKey when queryHash is not provided"
    /// When QueryHash is omitted, Build() derives it from the key via the hasher.
    /// </summary>
    [Fact]
    public void Build_Should_ComputeQueryHash_WhenNotProvided()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        QueryKey key = ["computed-hash-test"];
        var options = new QueryConfiguration<string> { QueryKey = key, GcTime = QueryTimeDefaults.GcTime };

        // Act
        var query = cache.Build<string, string>(client, options);

        // Assert
        var expectedHash = DefaultQueryKeyHasher.Instance.HashQueryKey(key);
        Assert.Equal(expectedHash, query.QueryHash);
    }

    /// <summary>
    /// TanStack: "should use provided queryHash instead of computing it"
    /// When QueryHash is explicitly set, Build() uses it verbatim.
    /// </summary>
    [Fact]
    public void Build_Should_UseProvidedQueryHash_InsteadOfComputing()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        QueryKey key = ["custom-hash-test"];
        var customHash = "custom-hash";
        var options = new QueryConfiguration<string> { QueryKey = key, QueryHash = customHash, GcTime = QueryTimeDefaults.GcTime };

        // Act
        var query = cache.Build<string, string>(client, options);

        // Assert
        Assert.Equal(customHash, query.QueryHash);
        Assert.NotEqual(DefaultQueryKeyHasher.Instance.HashQueryKey(key), query.QueryHash);
    }

    #endregion

    #region Add / Remove / Clear

    [Fact]
    public void Add_Should_Not_Add_Duplicate_Query()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        var cache = client.GetQueryCache();

        // Act — adding the same query again should be a no-op
        cache.Add(query);

        // Assert — still only one query
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void Remove_Should_Remove_Query_From_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        var cache = client.GetQueryCache();

        // Act
        cache.Remove(query);

        // Assert
        Assert.Empty(cache.GetAll());
        Assert.Null(cache.Get<string>(query.QueryHash!));
    }

    [Fact]
    public void Clear_Should_Empty_The_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        BuildQuery<string>(client, ["todos"]);
        BuildQuery<int>(client, ["count"]);
        var cache = client.GetQueryCache();
        Assert.Equal(2, cache.GetAll().Count());

        // Act
        cache.Clear();

        // Assert
        Assert.Empty(cache.GetAll());
    }

    #endregion

    #region Notify events

    [Fact]
    public void Notify_Should_Fire_QueryCacheQueryAddedEvent_When_Query_Built()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetQueryCache();
        QueryCacheNotifyEvent? receivedEvent = null;

        cache.Subscribe(@event => receivedEvent = @event);

        // Act
        BuildQuery<string>(client, ["todos"]);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.IsType<QueryCacheQueryAddedEvent>(receivedEvent);
    }

    [Fact]
    public void Notify_Should_Fire_QueryCacheQueryRemovedEvent_When_Query_Removed()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        var cache = client.GetQueryCache();
        QueryCacheNotifyEvent? receivedEvent = null;

        cache.Subscribe(@event => receivedEvent = @event);

        // Act
        cache.Remove(query);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.IsType<QueryCacheQueryRemovedEvent>(receivedEvent);
    }

    [Fact]
    public async Task Notify_Should_Fire_QueryCacheQueryUpdatedEvent_When_Query_Fetched()
    {
        // Arrange
        var client = CreateQueryClient();
        var query = BuildQuery<string>(client, ["todos"]);
        query.SetQueryFn(async _ => "data");
        var cache = client.GetQueryCache();
        var updatedEvents = new List<QueryCacheNotifyEvent>();

        cache.Subscribe(@event => updatedEvents.Add(@event));

        // Act
        await query.Fetch();

        // Assert — fetching dispatches FetchAction then SetStateAction, both produce update events
        Assert.True(updatedEvents.Count >= 2);
        Assert.All(updatedEvents, e => Assert.IsType<QueryCacheQueryUpdatedEvent>(e));
    }

    #endregion

    #region OnFocus / OnOnline

    [Fact]
    public async Task OnFocus_Should_Trigger_Refetch_On_Queries_With_QueryFn()
    {
        // Arrange — queries need an active observer for focus refetch (per-observer
        // RefetchOnWindowFocus defaults to WhenStale, and StaleTime 0 = always stale).
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return "data";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.GetQueryCache().OnFocus();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "OnFocus should trigger refetch");

        sub.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Trigger_Refetch_On_Queries_With_QueryFn()
    {
        // Arrange — queries need an active observer for online refetch.
        var client = CreateQueryClient();
        var initialFetch = new TaskCompletionSource();
        var refetch = new TaskCompletionSource();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["todos"],
                QueryFn = async _ =>
                {
                    var count = Interlocked.Increment(ref fetchCount);
                    if (count == 1) initialFetch.TrySetResult();
                    else refetch.TrySetResult();
                    return "data";
                },
                Enabled = true
            });

        var sub = observer.Subscribe(_ => { });
        await initialFetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.GetQueryCache().OnOnline();
        await refetch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fetchCount >= 2, "OnOnline should trigger refetch");

        sub.Dispose();
    }

    #endregion
}
