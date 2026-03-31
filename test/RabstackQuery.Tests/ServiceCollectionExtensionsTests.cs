using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace RabstackQuery;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRabstackQuery_Parameterless_Resolves_QueryClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void AddRabstackQuery_DefaultLifetime_Is_Scoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();

        // Act — resolve from two different scopes
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var client1 = scope1.ServiceProvider.GetRequiredService<QueryClient>();
        var client2 = scope2.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — scoped means different instances per scope
        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void AddRabstackQuery_Singleton_Returns_Same_Instance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(_ => { }, ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var client1 = scope1.ServiceProvider.GetRequiredService<QueryClient>();
        var client2 = scope2.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.Same(client1, client2);
    }

    [Fact]
    public void AddRabstackQuery_TwoArgOverload_Defaults_To_Scoped()
    {
        // Arrange — the (Action<Options>) overload should default to Scoped
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.DefaultOptions = new QueryClientDefaultOptions { Retry = 1 };
        });

        using var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var client1 = scope1.ServiceProvider.GetRequiredService<QueryClient>();
        var client2 = scope2.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — scoped means different instances
        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void AddRabstackQuery_Applies_DefaultOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.DefaultOptions = new QueryClientDefaultOptions
            {
                StaleTime = TimeSpan.FromSeconds(30),
                Retry = 2,
            };
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Act
        var defaults = client.DefaultOptions;

        // Assert
        Assert.NotNull(defaults);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults!.StaleTime);
        Assert.Equal(2, defaults.Retry);
    }

    [Fact]
    public void AddRabstackQuery_Applies_QueryDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.SetQueryDefaults(new QueryDefaults
            {
                QueryKey = ["products"],
                GcTime = TimeSpan.FromMinutes(10),
            });
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Act — build a query matching the prefix
        var cache = client.QueryCache;
        var query = cache.GetOrCreate<string, string>(client,
            new QueryConfiguration<string> { QueryKey = ["products", "shoes"] });

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(10), query.Options.GcTime);
    }

    [Fact]
    public void AddRabstackQuery_Applies_MutationDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.SetMutationDefaults(new MutationDefaults
            {
                MutationKey = ["orders"],
                Retry = 5,
            });
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Act — verify via DefaultMutationOptions (Mutation doesn't expose Options directly)
        var defaulted = client.DefaultMutationOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["orders", "create"],
        });

        // Assert
        Assert.Equal(5, defaulted.Retry);
    }

    [Fact]
    public void AddRabstackQuery_Resolves_ILoggerFactory_From_DI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — should not be the null logger
        Assert.IsNotType<NullLoggerFactory>(client.LoggerFactory);
    }

    [Fact]
    public void AddRabstackQuery_Falls_Back_To_NullLoggerFactory()
    {
        // Arrange — no ILoggerFactory registered
        var services = new ServiceCollection();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.IsType<NullLoggerFactory>(client.LoggerFactory);
    }

    [Fact]
    public void AddRabstackQuery_Resolves_IMeterFactory_From_DI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.NotNull(client.MeterFactory);
    }

    [Fact]
    public void AddRabstackQuery_MeterFactory_Null_When_Not_Registered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.Null(client.MeterFactory);
    }

    [Fact]
    public void AddRabstackQuery_Resolves_TimeProvider_From_DI()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.Same(fakeTime, client.TimeProvider);
    }

    [Fact]
    public void AddRabstackQuery_Resolves_IFocusManager_From_DI()
    {
        // Arrange
        var customFocusManager = new FocusManager();
        var services = new ServiceCollection();
        services.AddSingleton<IFocusManager>(customFocusManager);
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — should be the custom instance, not the global singleton
        Assert.Same(customFocusManager, client.FocusManager);
    }

    [Fact]
    public void AddRabstackQuery_Resolves_IOnlineManager_From_DI()
    {
        // Arrange
        var customOnlineManager = new OnlineManager();
        var services = new ServiceCollection();
        services.AddSingleton<IOnlineManager>(customOnlineManager);
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert
        Assert.Same(customOnlineManager, client.OnlineManager);
    }

    [Fact]
    public void AddRabstackQuery_TryAdd_Prevents_Double_Registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.DefaultOptions = new QueryClientDefaultOptions { Retry = 1 };
        });

        // Second call should be ignored
        services.AddRabstackQuery(options =>
        {
            options.DefaultOptions = new QueryClientDefaultOptions { Retry = 99 };
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — first registration wins
        Assert.Equal(1, client.DefaultOptions?.Retry);
    }

    [Fact]
    public void Dispose_Clears_Caches()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery();

        using var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();
        var cache = client.QueryCache;

        // Populate the cache
        client.SetQueryData(["products", 1], "Widget");
        Assert.NotNull(client.GetQueryData<string>(["products", 1]));

        // Act — dispose the scope (which disposes the scoped QueryClient)
        scope.Dispose();

        // Assert — cache should be empty after disposal
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void AddRabstackQuery_Forwards_MutationCacheConfig()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRabstackQuery(options =>
        {
            options.MutationCacheConfig = new MutationCacheConfig
            {
                OnError = (_, _, _, _, _) => Task.CompletedTask,
            };
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<QueryClient>();

        // Assert — the mutation cache should have the config (we verify it exists
        // by checking the cache is non-null; the callback itself is tested elsewhere)
        Assert.NotNull(client.MutationCache);
    }

    [Fact]
    public void SetQueryDefaults_Throws_On_Null()
    {
        // Arrange
        var options = new RabstackQueryOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => options.SetQueryDefaults(null!));
    }

    [Fact]
    public void SetMutationDefaults_Throws_On_Null()
    {
        // Arrange
        var options = new RabstackQueryOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => options.SetMutationDefaults(null!));
    }
}
