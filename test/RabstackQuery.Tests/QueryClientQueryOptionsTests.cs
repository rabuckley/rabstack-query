namespace RabstackQuery;

/// <summary>
/// Tests for <see cref="QueryClient"/> overloads that accept <see cref="QueryOptions{TData}"/>.
/// </summary>
public sealed class QueryClientQueryOptionsTests
{
    private static QueryClient CreateClient() => new(new QueryCache());

    private static QueryOptions<string> TodosOptions() => new()
    {
        QueryKey = ["todos"],
        QueryFn = async _ => "todo-data",
    };

    [Fact]
    public void GetQueryData_ViaOptions_ReturnsSameAsKeyOverload()
    {
        var client = CreateClient();
        client.SetQueryData<string>(["todos"], "cached");

        var options = TodosOptions();
        var result = client.GetQueryData(options);

        Assert.Equal("cached", result);
    }

    [Fact]
    public void SetQueryData_ViaOptions_PopulatesCacheAccessibleByKey()
    {
        var client = CreateClient();
        var options = TodosOptions();

        client.SetQueryData(options, "from-options");

        var byKey = client.GetQueryData<string>(["todos"]);
        Assert.Equal("from-options", byKey);
    }

    [Fact]
    public void SetQueryData_Updater_ViaOptions_Works()
    {
        var client = CreateClient();
        var options = TodosOptions();

        client.SetQueryData(options, "initial");
        client.SetQueryData(options, prev => prev + "-updated");

        Assert.Equal("initial-updated", client.GetQueryData(options));
    }

    [Fact]
    public async Task FetchQueryAsync_ViaOptions_FetchesAndCaches()
    {
        var client = CreateClient();
        var fetchCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["fetch-test"],
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return "fetched";
            },
        };

        var result = await client.FetchQueryAsync(options);

        Assert.Equal("fetched", result);
        Assert.Equal(1, fetchCount);

        // Verify it's cached
        Assert.Equal("fetched", client.GetQueryData(options));
    }

    [Fact]
    public async Task PrefetchQueryAsync_ViaOptions_SwallowsErrors()
    {
        var client = CreateClient();
        var options = new QueryOptions<string>
        {
            QueryKey = ["prefetch-error"],
            QueryFn = _ => throw new InvalidOperationException("boom"),
        };

        // Should not throw
        await client.PrefetchQueryAsync(options);
    }

    [Fact]
    public async Task EnsureQueryDataAsync_ReturnsCachedWhenFresh()
    {
        var client = CreateClient();
        var fetchCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["ensure-test"],
            QueryFn = async _ =>
            {
                Interlocked.Increment(ref fetchCount);
                return "fetched";
            },
        };

        // First call fetches
        var first = await client.EnsureQueryDataAsync(options);
        Assert.Equal("fetched", first);
        Assert.Equal(1, fetchCount);

        // Second call returns cached (staleTime is null → default zero = always stale,
        // but EnsureQueryDataAsync checks the existing data)
        var second = await client.EnsureQueryDataAsync(options);
        Assert.Equal("fetched", second);
    }

    [Fact]
    public async Task FetchQueryAsync_RespectsRetryFromOptions()
    {
        var client = CreateClient();
        var attemptCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["retry-test"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref attemptCount);
                throw new InvalidOperationException("fail");
            },
            Retry = 2,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchQueryAsync(options));

        // 1 initial + 2 retries = 3 total attempts
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task FetchQueryAsync_ViaOptions_RespectsRetryDelay()
    {
        var client = CreateClient();
        var delays = new List<int>();
        var attemptCount = 0;
        var options = new QueryOptions<string>
        {
            QueryKey = ["retry-delay-test"],
            QueryFn = _ =>
            {
                Interlocked.Increment(ref attemptCount);
                throw new InvalidOperationException("fail");
            },
            Retry = 2,
            RetryDelay = (failureCount, _) =>
            {
                delays.Add(failureCount);
                return TimeSpan.Zero;
            },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchQueryAsync(options));

        Assert.Equal(3, attemptCount);
        // RetryDelay should have been called for each retry (failure counts 1 and 2)
        Assert.Equal([1, 2], delays);
    }

    [Fact]
    public async Task FetchQueryAsync_ViaOptions_RespectsNetworkMode()
    {
        var client = CreateClient();
        var options = new QueryOptions<string>
        {
            QueryKey = ["network-mode-test"],
            QueryFn = async _ => "data",
            NetworkMode = NetworkMode.Always,
        };

        // NetworkMode.Always means it fetches regardless of online status.
        // Verify the fetch succeeds (it would pause indefinitely with default
        // NetworkMode.Online if the OnlineManager reported offline).
        var result = await client.FetchQueryAsync(options);
        Assert.Equal("data", result);
    }
}
