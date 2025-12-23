namespace RabstackQuery.Tests;

public class MutationDefaultsTests
{
    [Fact]
    public void SetMutationDefaults_AppliesRetry_ToMatchingMutations()
    {
        // Arrange
        var client = CreateQueryClient();
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            Retry = 3,
        });

        // Act — build a mutation that matches the prefix
        var cache = client.GetMutationCache();
        var mutation = cache.Build<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["todos", "create"],
                MutationFn = (_, _, _) => Task.FromResult("ok"),
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
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
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
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            Retry = 3,
        });

        // Per-mutation Retry should win over key defaults
        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
            Retry = 5,
        });

        Assert.Equal(5, defaulted.Retry);
    }

    [Fact]
    public void SetMutationDefaults_MultipleDefaults_MergeInOrder()
    {
        var client = CreateQueryClient();

        // Register a broad default first
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            Retry = 2,
            GcTime = TimeSpan.FromMilliseconds(10_000),
        });

        // Register a more specific default that overrides Retry but not GcTime
        client.SetMutationDefaults(["todos", "create"], new MutationDefaults
        {
            MutationKey = ["todos", "create"],
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

        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            Retry = 2,
        });

        // Replace with new defaults for the same key
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
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
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
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
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
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
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            GcTime = TimeSpan.FromMilliseconds(10_000),
        });

        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["todos", "create"],
        });

        Assert.Equal(TimeSpan.FromMilliseconds(10_000), defaulted.GcTime);
    }

    [Fact]
    public void GetMutationDefaults_ReturnsNull_WhenNoDefaultsMatch()
    {
        var client = CreateQueryClient();

        var result = client.GetMutationDefaults(["nonexistent"]);

        Assert.Null(result);
    }

    [Fact]
    public void GetMutationDefaults_ReturnsMergedDefaults_WhenMatching()
    {
        var client = CreateQueryClient();
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
            Retry = 3,
            GcTime = TimeSpan.FromMilliseconds(10_000),
        });

        var result = client.GetMutationDefaults(["todos", "create"]);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Retry);
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), result.GcTime);
    }

    [Fact]
    public void DefaultMutationOptions_IsIdempotent()
    {
        // Applying defaults twice should produce the same result
        var client = CreateQueryClient();
        client.SetMutationDefaults(["todos"], new MutationDefaults
        {
            MutationKey = ["todos"],
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
            MutationKey = ["todos"],
            OnMutate = onMutate,
            MutationFn = (vars, _, ct) => Task.FromResult("ok"),
        };

        var defaulted = client.DefaultMutationOptions(options);

        Assert.Same(onMutate, defaulted.OnMutate);
        Assert.NotNull(defaulted.MutationFn);
    }

    private static QueryClient CreateQueryClient()
    {
        return new QueryClient(new QueryCache());
    }
}
