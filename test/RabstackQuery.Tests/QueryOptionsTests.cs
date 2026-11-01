namespace RabstackQuery;

/// <summary>
/// Tests for the new <see cref="QueryOptions{TData}"/> type: construction,
/// implicit conversion, and nullable-property defaults.
/// </summary>
public sealed class QueryOptionsTests
{
    [Fact]
    public void Construction_WithRequiredProperties_Succeeds()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ => "data",
        };

        Assert.Equal<QueryKey>(["todos"], options.QueryKey);
        Assert.NotNull(options.QueryFn);
    }

    [Fact]
    public void NullableProperties_DefaultToNull()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ => "data",
        };

        Assert.Null(options.StaleTime);
        Assert.Null(options.GcTime);
        Assert.Null(options.Retry);
        Assert.Null(options.RetryDelay);
        Assert.Null(options.NetworkMode);
        Assert.Null(options.Meta);
    }

    [Fact]
    public void ImplicitConversion_ToQueryKey_ReturnsQueryKey()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["projects", 42],
            QueryFn = async _ => "data",
        };

        QueryKey key = options;

        Assert.Equal<QueryKey>(["projects", 42], key);
    }

    [Fact]
    public void ToFetchQueryOptions_MapsProperties()
    {
        var options = new QueryOptions<int>
        {
            QueryKey = ["count"],
            QueryFn = async _ => 42,
            StaleTime = TimeSpan.FromSeconds(30),
            GcTime = TimeSpan.FromMinutes(10),
            Retry = 2,
        };

        var fetchOptions = options.ToFetchQueryOptions();

        Assert.Equal<QueryKey>(["count"], fetchOptions.QueryKey);
        Assert.Equal(TimeSpan.FromSeconds(30), fetchOptions.StaleTime);
        Assert.Equal(TimeSpan.FromMinutes(10), fetchOptions.GcTime);
        Assert.Equal(2, fetchOptions.Retry);
    }

    [Fact]
    public void ToFetchQueryOptions_LeavesRetryUnset_WhenNull()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["todos"],
            QueryFn = async _ => "data",
        };

        var fetchOptions = options.ToFetchQueryOptions();

        Assert.Null(fetchOptions.Retry);
    }

    [Fact]
    public void ToFetchQueryOptions_MapsRetryDelay_Meta_NetworkMode()
    {
        Func<int, Exception, TimeSpan> retryDelay = (count, _) => TimeSpan.FromSeconds(count);
        var meta = new Meta(new Dictionary<string, object?> { ["source"] = "test" });
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "data",
            RetryDelay = retryDelay,
            Meta = meta,
            NetworkMode = NetworkMode.Always,
        };

        var fetchOptions = options.ToFetchQueryOptions();

        Assert.Same(retryDelay, fetchOptions.RetryDelay);
        Assert.Same(meta, fetchOptions.Meta);
        Assert.Equal(NetworkMode.Always, fetchOptions.NetworkMode);
    }

    [Fact]
    public void ToObserverOptions_MapsAllProperties()
    {
        Func<int, Exception, TimeSpan> retryDelay = (count, _) => TimeSpan.FromSeconds(count);
        var meta = new Meta(new Dictionary<string, object?> { ["key"] = "value" });
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "data",
            StaleTime = TimeSpan.FromSeconds(30),
            GcTime = TimeSpan.FromMinutes(10),
            Retry = 2,
            RetryDelay = retryDelay,
            NetworkMode = NetworkMode.OfflineFirst,
            Meta = meta,
        };

        var observer = options.ToObserverOptions();

        Assert.Equal<QueryKey>(["test"], observer.QueryKey);
        Assert.NotNull(observer.QueryFn);
        Assert.Equal(TimeSpan.FromSeconds(30), observer.StaleTime);
        Assert.Equal(TimeSpan.FromMinutes(10), observer.GcTime);
        Assert.Equal(2, observer.Retry);
        Assert.Same(retryDelay, observer.RetryDelay);
        Assert.Equal(NetworkMode.OfflineFirst, observer.NetworkMode);
        Assert.Same(meta, observer.Meta);
    }

    [Fact]
    public void ToObserverOptions_LeavesDefaultableOptionsUnset_WhenNull()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "data",
        };

        var observer = options.ToObserverOptions();

        Assert.Null(observer.StaleTime);
        Assert.Null(observer.GcTime);
        Assert.Null(observer.Retry);
        Assert.Null(observer.RetryDelay);
        Assert.Null(observer.Meta);
    }

    [Fact]
    public void ToObserverOptions_WithSelect_MapsProperties()
    {
        var meta = new Meta(new Dictionary<string, object?> { ["key"] = "value" });
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = async _ => "hello world",
            StaleTime = TimeSpan.FromSeconds(15),
            GcTime = TimeSpan.FromMinutes(3),
            Retry = 1,
            NetworkMode = NetworkMode.Always,
            Meta = meta,
        };

        var observer = options.ToObserverOptions(s => s.Length);

        Assert.Equal<QueryKey>(["test"], observer.QueryKey);
        Assert.NotNull(observer.Select);
        Assert.Equal(11, observer.Select!("hello world"));
        Assert.Equal(TimeSpan.FromSeconds(15), observer.StaleTime);
        Assert.Equal(TimeSpan.FromMinutes(3), observer.GcTime);
        Assert.Equal(1, observer.Retry);
        Assert.Equal(NetworkMode.Always, observer.NetworkMode);
        Assert.Same(meta, observer.Meta);
    }

    [Fact]
    public void ToObserverOptions_CarriesQueryFnHasherAndStructuralSharing()
    {
        Func<QueryFunctionContext, Task<string>> queryFn = _ => Task.FromResult("data");
        Func<string, string, string> sharing = (_, next) => next;
        var hasher = DefaultQueryKeyHasher.Instance;
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = queryFn,
            QueryKeyHasher = hasher,
            StructuralSharing = sharing,
        };

        var observer = options.ToObserverOptions();

        Assert.Same(queryFn, observer.QueryFn);
        Assert.Same(hasher, observer.QueryKeyHasher);
        Assert.Same(sharing, observer.StructuralSharing);
    }

    [Fact]
    public void ToObserverOptions_WithSelect_DoesNotCarrySourceStructuralSharing()
    {
        // Observers apply StructuralSharing to the selected output, so a delegate over the source
        // type cannot be reused once Select changes the output type.
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = _ => Task.FromResult("data"),
            QueryKeyHasher = DefaultQueryKeyHasher.Instance,
            StructuralSharing = (_, next) => next,
        };

        var observer = options.ToObserverOptions(s => s.Length);

        Assert.Same(options.QueryFn, observer.QueryFn);
        Assert.Same(options.QueryKeyHasher, observer.QueryKeyHasher);
        Assert.Null(observer.StructuralSharing);
    }

    [Fact]
    public void ToObserverOptions_WithExpression_AddsObserverSettings_WithoutChangingDefinition()
    {
        var options = new QueryOptions<string>
        {
            QueryKey = ["test"],
            QueryFn = _ => Task.FromResult("data"),
            StaleTime = TimeSpan.FromSeconds(15),
        };

        var first = options.ToObserverOptions() with { Enabled = false, StaleTime = TimeSpan.Zero };
        var second = options.ToObserverOptions();

        Assert.False(first.Enabled);
        Assert.Equal(TimeSpan.Zero, first.StaleTime);
        Assert.True(second.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(15), second.StaleTime);
        Assert.Equal(TimeSpan.FromSeconds(15), options.StaleTime);
    }

    [Fact]
    public void ToObserverOptions_NullSelect_Throws()
    {
        var options = QueryOptions.Create(["test"], _ => Task.FromResult("data"));
        Func<string, int>? select = null;

        void Act() => options.ToObserverOptions(select!);

        Assert.Throws<ArgumentNullException>("select", Act);
    }
}
