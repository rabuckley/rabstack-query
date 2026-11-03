namespace RabstackQuery;

public sealed class MutationOptionsFactoryTests
{
    [Fact]
    public async Task Create_InfersTypesFromTypedLambda_AndProducesRunnableOptions()
    {
        using var client = new QueryClient(new QueryCache());

        var options = MutationOptions.Create(
            (int variables, MutationFunctionContext _) =>
                Task.FromResult($"result:{variables}"));

        var observer = MutationObserver.Create(client, options);
        var result = await observer.MutateAsync(42);

        Assert.IsType<MutationOptions<string, int>>(options);
        Assert.Equal("result:42", result);
    }

    [Fact]
    public async Task Create_AcceptsMethodGroup_WithExplicitTypeArguments()
    {
        using var client = new QueryClient(new QueryCache());

        var options = MutationOptions.Create<string, int>(Describe);

        var result = await MutationObserver.Create(client, options).MutateAsync(7);

        Assert.Equal("7", result);

        static Task<string> Describe(int value, MutationFunctionContext context) =>
            Task.FromResult(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CreateOptimistic_InfersRollbackType_FromUntypedOnMutate()
    {
        using var client = new QueryClient(new QueryCache());
        string? rollbackOnSuccess = null;
        int? variablesOnSuccess = null;

        var options = MutationOptions.Create(
            (int variables, MutationFunctionContext _) => Task.FromResult(variables * 2),
            onMutate: (variables, _) => Task.FromResult($"snapshot:{variables}"))
            with
            {
                OnSuccess = (_, variables, rollback, _) =>
                {
                    variablesOnSuccess = variables;
                    rollbackOnSuccess = rollback;
                    return Task.CompletedTask;
                },
            };

        var result = await new MutationObserver<int, Exception, int, string>(client, options).MutateAsync(21);

        Assert.IsType<MutationOptions<int, Exception, int, string>>(options);
        Assert.Equal(42, result);
        Assert.Equal(21, variablesOnSuccess);
        Assert.Equal("snapshot:21", rollbackOnSuccess);
    }

    [Fact]
    public async Task CreateOptimistic_PassesRollbackStateToErrorAndSettled_OnFailure()
    {
        using var client = new QueryClient(new QueryCache());
        var failure = new InvalidOperationException("boom");
        string? rollbackOnError = null;
        string? rollbackOnSettled = null;
        Exception? errorOnSettled = null;

        var options = MutationOptions.Create(
            (int _, MutationFunctionContext _) => Task.FromException<int>(failure),
            onMutate: (variables, _) => Task.FromResult($"snapshot:{variables}"))
            with
            {
                OnError = (_, _, rollback, _) =>
                {
                    rollbackOnError = rollback;
                    return Task.CompletedTask;
                },
                OnSettled = (_, error, _, rollback, _) =>
                {
                    errorOnSettled = error;
                    rollbackOnSettled = rollback;
                    return Task.CompletedTask;
                },
            };

        var observer = new MutationObserver<int, Exception, int, string>(client, options);
        Task Act() => observer.MutateAsync(5);

        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Equal("snapshot:5", rollbackOnError);
        Assert.Equal("snapshot:5", rollbackOnSettled);
        Assert.Same(failure, errorOnSettled);
    }

    [Fact]
    public void Create_LeavesDefaultableSettingsUnset_SoDefaultsApply()
    {
        var options = MutationOptions.Create(
            (int _, MutationFunctionContext _) => Task.FromResult("ok"));

        Assert.Null(options.Retry);
        Assert.Null(options.RetryDelay);
        Assert.Null(options.GcTime);
        Assert.Null(options.NetworkMode);
        Assert.Null(options.MutationKey);
        Assert.Null(options.OnMutate);
    }

    [Fact]
    public void Create_NullMutationFn_Throws()
    {
        Func<int, MutationFunctionContext, Task<string>>? mutationFn = null;

        void Act() => MutationOptions.Create(mutationFn!);

        Assert.Throws<ArgumentNullException>("mutationFn", Act);
    }

    [Fact]
    public void CreateOptimistic_NullOnMutate_Throws()
    {
        Func<int, MutationFunctionContext, Task<string>>? onMutate = null;

        void Act() => MutationOptions.Create(
            (int _, MutationFunctionContext _) => Task.FromResult("ok"),
            onMutate!);

        Assert.Throws<ArgumentNullException>("onMutate", Act);
    }
}
