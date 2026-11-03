namespace RabstackQuery;

public class MutationDefaultsTests
{
    [Fact]
    public void SetMutationDefaults_AppliesRetry_ToMatchingMutations()
    {
        // Arrange
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 3,
            });

        // Act — build a mutation that matches the prefix
        var cache = client.MutationCache;

        var mutation = cache.GetOrCreate(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "create"], MutationFn = (_, _) => Task.FromResult("ok"),
            });

        // Assert — cannot directly inspect options on Mutation, but we can verify
        // via DefaultMutationOptions
        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(3, defaulted.Retry);
    }

    [Fact]
    public void SetMutationDefaults_DoesNotApply_ToNonMatchingMutations()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 3,
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["users", "create"],
        });

        // Should get the default 0 retries
        Assert.Equal(0, defaulted.Retry);
    }

    [Fact]
    public void SetMutationDefaults_PerMutationOverrides_KeyDefaults()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 3,
            });

        // Per-mutation Retry should win over key defaults
        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"], Retry = 5,
        });

        Assert.Equal(5, defaulted.Retry);
    }

    [Fact]
    public void SetMutationDefaults_MultipleDefaults_MergeInOrder()
    {
        var client = CreateQueryClient();

        // Register a broad default first
        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 2, GcTime = TimeSpan.FromMilliseconds(10_000),
            });

        // Register a more specific default that overrides Retry but not GcTime
        client.SetMutationDefaults(["todos", "create"],
            new MutationDefaults
            {
                Retry = 5,
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        // Retry comes from the more specific default (5)
        Assert.Equal(5, defaulted.Retry);
        // GcTime comes from the broader default (10_000)
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), defaulted.GcTime);
    }

    [Fact]
    public void SetMutationDefaults_ReplacesExisting_ForSameKey()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 2,
            });

        // Replace with new defaults for the same key
        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 5,
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(5, defaulted.Retry);
    }

    [Fact]
    public void SetMutationDefaults_AppliesRetryDelay()
    {
        var client = CreateQueryClient();

        Func<int, Exception, TimeSpan> customDelay = (count, _) => TimeSpan.FromMilliseconds(count * 500);

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                RetryDelay = customDelay,
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Same(customDelay, defaulted.RetryDelay);
    }

    [Fact]
    public void SetMutationDefaults_AppliesNetworkMode()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                NetworkMode = NetworkMode.Always,
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(NetworkMode.Always, defaulted.NetworkMode);
    }

    [Fact]
    public void SetMutationDefaults_AppliesGcTime()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                GcTime = TimeSpan.FromMilliseconds(10_000),
            });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(TimeSpan.FromMilliseconds(10_000), defaulted.GcTime);
    }

    [Fact]
    public void GetMutationDefaults_ReturnsEmptyDefaults_WhenNoDefaultsMatch()
    {
        var client = CreateQueryClient();

        var result = client.GetMutationDefaults(["nonexistent"]);

        Assert.Equal(new MutationDefaults(), result);
    }

    [Fact]
    public void GetMutationDefaults_ReturnsMergedDefaults_WhenMatching()
    {
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 3, GcTime = TimeSpan.FromMilliseconds(10_000),
            });

        var result = client.GetMutationDefaults(["todos", "create"]);

        Assert.Equal(3, result.Retry);
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), result.GcTime);
    }

    [Fact]
    public void DefaultMutationOptions_IsIdempotent()
    {
        // Applying defaults twice should produce the same result
        var client = CreateQueryClient();

        client.SetMutationDefaults(["todos"],
            new MutationDefaults
            {
                Retry = 3,
            });

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        };

        var first = client.DefaultMutationOptions(options);
        var second = client.DefaultMutationOptions(first);

        // Second call should short-circuit and return the same instance
        Assert.Same(first, second);
        Assert.Equal(3, second.Retry);
    }

    [Fact]
    public void DefaultMutationOptions_PreservesCallbacks()
    {
        // Callbacks should not be lost during defaulting
        var client = CreateQueryClient();

        Func<string, MutationFunctionContext, Task<object?>> onMutate =
            (_, _) => Task.FromResult<object?>(null);

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos"], OnMutate = onMutate, MutationFn = (vars, _) => Task.FromResult("ok"),
        };

        var defaulted = client.DefaultMutationOptions(options);

        Assert.Same(onMutate, defaulted.OnMutate);
        Assert.NotNull(defaulted.MutationFn);
    }


    [Fact]
    public void ClientDefaults_ApplyToMutations()
    {
        var client = CreateQueryClient();
        client.DefaultOptions = new QueryClientDefaultOptions
        {
            Mutations = new MutationDefaults { Retry = 2, NetworkMode = NetworkMode.Always },
        };

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>());

        Assert.Equal(2, defaulted.Retry);
        Assert.Equal(NetworkMode.Always, defaulted.NetworkMode);
    }

    [Fact]
    public void KeyPrefixDefaults_OverrideClientDefaults()
    {
        var client = CreateQueryClient();
        client.DefaultOptions = new QueryClientDefaultOptions
        {
            Mutations = new MutationDefaults { Retry = 2, GcTime = TimeSpan.FromSeconds(1) },
        };
        client.SetMutationDefaults(["todos"], new MutationDefaults { Retry = 4 });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(4, defaulted.Retry);
        Assert.Equal(TimeSpan.FromSeconds(1), defaulted.GcTime);
    }

    [Fact]
    public void ExplicitOnlineNetworkMode_OverridesKeyPrefixDefault()
    {
        var client = CreateQueryClient();
        client.SetMutationDefaults(["todos"], new MutationDefaults { NetworkMode = NetworkMode.Always });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos"],
            NetworkMode = NetworkMode.Online,
        });

        Assert.Equal(NetworkMode.Online, defaulted.NetworkMode);
    }

    [Fact]
    public void ExplicitFiveMinuteGcTime_OverridesKeyPrefixDefault()
    {
        var client = CreateQueryClient();
        client.SetMutationDefaults(["todos"], new MutationDefaults { GcTime = TimeSpan.FromSeconds(1) });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos"],
            GcTime = TimeSpan.FromMinutes(5),
        });

        Assert.Equal(TimeSpan.FromMinutes(5), defaulted.GcTime);
    }

    [Fact]
    public async Task ExplicitOnlineNetworkMode_PausesOfflineMutation_DespiteKeyPrefixAlways()
    {
        var onlineManager = new OnlineManager();
        onlineManager.SetOnline(false);
        using var client = new QueryClient(new QueryCache(), onlineManager: onlineManager);
        client.SetMutationDefaults(["todos"], new MutationDefaults { NetworkMode = NetworkMode.Always });
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cacheSubscription = client.MutationCache.Subscribe(_ =>
        {
            if (client.MutationCache.GetAll().Any(m => m.CurrentIsPaused))
            {
                paused.TrySetResult();
            }
        });
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos"],
                NetworkMode = NetworkMode.Online,
                MutationFn = (input, _) => Task.FromResult(input),
            });

        var mutation = observer.MutateAsync("todo");
        await paused.Task;

        Assert.False(mutation.IsCompleted);
        onlineManager.SetOnline(true);
        Assert.Equal("todo", await mutation);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
