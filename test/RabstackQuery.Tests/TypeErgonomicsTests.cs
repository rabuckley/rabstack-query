namespace RabstackQuery;

public class TypeErgonomicsTests
{
    [Fact]
    public void MutationOptions_TwoParamAlias_IsAssignableToFourParam()
    {
        var twoParam = new MutationOptions<string, int>
        {
            MutationFn = async (v, ctx, ct) => v.ToString()
        };

        MutationOptions<string, Exception, int, object?> fourParam = twoParam;

        Assert.NotNull(fourParam.MutationFn);
    }

    [Fact]
    public void MutateOptions_FourParamConstruction_Works()
    {
        var options = new MutateOptions<string, Exception, int, object?>
        {
            OnSuccess = async (data, variables, context, fnCtx) => { }
        };

        Assert.NotNull(options.OnSuccess);
    }

    [Fact]
    public async Task QueryObserver_SingleParamAlias_CanObserveQuery()
    {
        using var client = new QueryClient(new QueryCache());
        var fetchCompleted = new TaskCompletionSource<string>();

        var observer = new QueryObserver<string>(
            client,
            new QueryObserverOptions<string>
            {
                QueryKey = ["ergonomics", "observer"],
                QueryFn = async _ => "observed-value"
            });

        string? receivedData = null;
        using var subscription = observer.Subscribe(result =>
        {
            if (result.Data is { } data)
            {
                receivedData = data;
                fetchCompleted.TrySetResult(data);
            }
        });

        var result = await fetchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("observed-value", result);
        Assert.Equal("observed-value", receivedData);
    }

    [Fact]
    public void QueryOptions_Create_InfersType()
    {
        var options = QueryOptions.Create(
            ["ergonomics", "query"],
            async ctx => 42);

        Assert.IsType<QueryOptions<int>>(options);
        Assert.NotNull(options.QueryFn);
    }

    [Fact]
    public void FetchQueryOptions_Create_InfersType()
    {
        var options = FetchQueryOptions.Create(
            ["ergonomics", "fetch"],
            async ctx => "fetched");

        Assert.IsType<FetchQueryOptions<string>>(options);
        Assert.NotNull(options.QueryFn);
    }

    [Fact]
    public void QueryObserverOptions_Create_InfersType()
    {
        var options = QueryObserverOptions.Create(
            ["ergonomics", "observer"],
            async ctx => 3.14);

        Assert.IsType<QueryObserverOptions<double>>(options);
        Assert.NotNull(options.QueryFn);
    }

    [Fact]
    public void MutationDefinition_Create_InfersType()
    {
        var def = MutationDefinition.Create<string, int>(
            async (variables, ctx, ct) => variables.ToString());

        Assert.IsType<MutationDefinition<string, int>>(def);
        Assert.NotNull(def.MutationFn);
    }

    [Fact]
    public async Task MutationObserver_Create_CanExecuteMutation()
    {
        using var client = new QueryClient(new QueryCache());

        var options = new MutationOptions<string, string>
        {
            MutationFn = async (variables, context, ct) => $"result:{variables}"
        };

        var observer = MutationObserver.Create(client, options);

        var result = await observer.MutateAsync("input");

        Assert.Equal("result:input", result);
    }
}
