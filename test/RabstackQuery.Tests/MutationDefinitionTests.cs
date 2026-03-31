namespace RabstackQuery;

public sealed class MutationDefinitionTests
{
    [Fact]
    public void ToMutationOptions_MapsAllConfigProperties()
    {
        var retryDelay = (int count, Exception ex) => TimeSpan.FromSeconds(count);
        var meta = new Meta();
        var scope = new MutationScope("test");

        var def = new MutationDefinition<string, int>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult("ok"),
            MutationKey = ["mutations", "test"],
            Retry = 3,
            RetryDelay = retryDelay,
            GcTime = TimeSpan.FromMinutes(10),
            NetworkMode = NetworkMode.Always,
            Meta = meta,
            Scope = scope,
        };

        var options = def.ToMutationOptions();

        Assert.NotNull(options.MutationFn);
        Assert.Equal(["mutations", "test"], options.MutationKey);
        Assert.Equal(3, options.Retry);
        Assert.Same(retryDelay, options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), options.GcTime);
        Assert.Equal(NetworkMode.Always, options.NetworkMode);
        Assert.Same(meta, options.Meta);
        Assert.Same(scope, options.Scope);
    }

    [Fact]
    public void ToMutationOptions_WithoutCallbacks_HasNoLifecycleHooks()
    {
        var def = new MutationDefinition<string, int>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult("ok"),
        };

        var options = def.ToMutationOptions();

        Assert.Null(options.OnMutate);
        Assert.Null(options.OnSuccess);
        Assert.Null(options.OnError);
        Assert.Null(options.OnSettled);
    }

    [Fact]
    public async Task ToMutationOptions_WithCallbacks_AdaptsLifecycleHooks()
    {
        var successCalled = false;
        var errorCalled = false;
        var settledCalled = false;

        var def = new MutationDefinition<string, int>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult("ok"),
        };

        var callbacks = new MutationCallbacks<string, int>
        {
            OnSuccess = (data, vars, ctx) =>
            {
                successCalled = true;
                return Task.CompletedTask;
            },
            OnError = (err, vars, ctx) =>
            {
                errorCalled = true;
                return Task.CompletedTask;
            },
            OnSettled = (data, err, vars, ctx) =>
            {
                settledCalled = true;
                return Task.CompletedTask;
            },
        };

        var options = def.ToMutationOptions(callbacks);

        // The adapted delegates should forward to the callbacks, ignoring the
        // TOnMutateResult? parameter (4th position in the 4-param signatures).
        Assert.NotNull(options.OnSuccess);
        Assert.NotNull(options.OnError);
        Assert.NotNull(options.OnSettled);

        var fakeContext = new MutationFunctionContext(null!, null, null);
        await options.OnSuccess!("data", 42, null, fakeContext);
        await options.OnError!(new InvalidOperationException(), 42, null, fakeContext);
        await options.OnSettled!("data", null, 42, null, fakeContext);

        Assert.True(successCalled);
        Assert.True(errorCalled);
        Assert.True(settledCalled);
    }

    [Fact]
    public void ToMutationOptions_UsesDefaults_WhenNotSet()
    {
        var def = new MutationDefinition<string, int>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult("ok"),
        };

        var options = def.ToMutationOptions();

        Assert.Equal(QueryTimeDefaults.GcTime, options.GcTime);
        Assert.Equal(NetworkMode.Online, options.NetworkMode);
        Assert.Null(options.MutationKey);
        Assert.Null(options.Retry);
    }

    [Fact]
    public void OptimisticDef_ToMutationOptions_MapsAllProperties()
    {
        var retryDelay = (int count, Exception ex) => TimeSpan.FromSeconds(count);
        Func<int, MutationFunctionContext, Task<string>> onMutate = (vars, ctx) => Task.FromResult("snapshot");

        Func<string, int, string?, MutationFunctionContext, Task> onSuccess = (data, vars, ctx, fctx) =>
            Task.CompletedTask;

        Func<Exception, int, string?, MutationFunctionContext, Task> onError = (err, vars, ctx, fctx) =>
            Task.CompletedTask;

        Func<string?, Exception?, int, string?, MutationFunctionContext, Task> onSettled =
            (data, err, vars, ctx, fctx) => Task.CompletedTask;

        var def = new OptimisticMutationDefinition<string, int, string>
        {
            MutationFn = (vars, ctx, ct) => Task.FromResult("ok"),
            OnMutate = onMutate,
            OnSuccess = onSuccess,
            OnError = onError,
            OnSettled = onSettled,
            MutationKey = ["test"],
            Retry = 2,
            RetryDelay = retryDelay,
            GcTime = TimeSpan.FromMinutes(15),
            NetworkMode = NetworkMode.OfflineFirst,
        };

        var options = def.ToMutationOptions();

        Assert.NotNull(options.MutationFn);
        Assert.Same(onMutate, options.OnMutate);
        Assert.Same(onSuccess, options.OnSuccess);
        Assert.Same(onError, options.OnError);
        Assert.Same(onSettled, options.OnSettled);
        Assert.Equal(["test"], options.MutationKey);
        Assert.Equal(2, options.Retry);
        Assert.Same(retryDelay, options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(15), options.GcTime);
        Assert.Equal(NetworkMode.OfflineFirst, options.NetworkMode);
    }

    [Fact]
    public void Create_InfersTypesFromDelegate()
    {
        // Verify that the static factory compiles without explicit generic parameters.
        // TData=string, TVariables=int inferred from the lambda.
        var def = MutationDefinition.Create((int vars, MutationFunctionContext ctx, CancellationToken ct) =>
            Task.FromResult("result"));

        Assert.NotNull(def.MutationFn);
    }

    [Fact]
    public void OptimisticCreate_InfersTypesFromDelegates()
    {
        // TData=string, TVariables=int, TOnMutateResult=bool inferred from the two lambdas.
        var def = OptimisticMutationDefinition.Create(
            (int vars, MutationFunctionContext ctx, CancellationToken ct) => Task.FromResult("result"),
            (int vars, MutationFunctionContext ctx) => Task.FromResult(true));

        Assert.NotNull(def.MutationFn);
        Assert.NotNull(def.OnMutate);
    }
}
