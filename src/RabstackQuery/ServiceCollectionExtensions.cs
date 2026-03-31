using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Extension methods for registering RabStack Query services with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="QueryClient"/> with default settings and
    /// <see cref="ServiceLifetime.Scoped"/> lifetime.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRabstackQuery(this IServiceCollection services)
        => services.AddRabstackQuery(_ => { });

    /// <summary>
    /// Registers a <see cref="QueryClient"/> with
    /// <see cref="ServiceLifetime.Scoped"/> lifetime, configured by the
    /// <paramref name="configure"/> delegate.
    /// </summary>
    /// <inheritdoc cref="AddRabstackQuery(IServiceCollection, Action{RabstackQueryOptions}, ServiceLifetime)"/>
    public static IServiceCollection AddRabstackQuery(
        this IServiceCollection services,
        Action<RabstackQueryOptions> configure)
        => services.AddRabstackQuery(configure, ServiceLifetime.Scoped);

    /// <summary>
    /// Registers a <see cref="QueryClient"/> configured by the
    /// <paramref name="configure"/> delegate with the specified
    /// <paramref name="lifetime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="QueryClient"/> resolves <see cref="ILoggerFactory"/>,
    /// <see cref="System.Diagnostics.Metrics.IMeterFactory"/>, and
    /// <see cref="TimeProvider"/> from DI when available.
    /// <see cref="IFocusManager"/> and <see cref="IOnlineManager"/> are
    /// resolved from DI if registered; otherwise the global singletons are used.
    /// </para>
    /// <para>
    /// <see cref="ServiceLifetime.Scoped"/> is correct for both Blazor WASM
    /// (scope = app lifetime) and Blazor Server (scope = circuit). Use
    /// <see cref="ServiceLifetime.Singleton"/> for hosts where a single shared
    /// cache is desired (e.g. MAUI).
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure query client options.</param>
    /// <param name="lifetime">
    /// The DI service lifetime. Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddRabstackQuery(
        this IServiceCollection services,
        Action<RabstackQueryOptions> configure,
        ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RabstackQueryOptions();
        configure(options);

        services.TryAdd(new ServiceDescriptor(
            typeof(QueryClient),
            sp => CreateQueryClient(sp, options),
            lifetime));

        return services;
    }

    private static QueryClient CreateQueryClient(
        IServiceProvider sp,
        RabstackQueryOptions options)
    {
        var queryCache = new QueryCache();
        var mutationCache = new MutationCache(options.MutationCacheConfig);

        var client = new QueryClient(
            queryCache,
            mutationCache,
            timeProvider: sp.GetService<TimeProvider>(),
            focusManager: sp.GetService<IFocusManager>(),
            onlineManager: sp.GetService<IOnlineManager>(),
            loggerFactory: sp.GetService<ILoggerFactory>(),
            meterFactory: sp.GetService<System.Diagnostics.Metrics.IMeterFactory>());

        if (options.DefaultOptions is not null)
        {
            client.DefaultOptions = options.DefaultOptions;
        }

        if (options.QueryDefaults is not null)
        {
            foreach (var defaults in options.QueryDefaults)
            {
                client.SetQueryDefaults(defaults.QueryKey, defaults);
            }
        }

        if (options.MutationDefaults is not null)
        {
            foreach (var defaults in options.MutationDefaults)
            {
                client.SetMutationDefaults(defaults.MutationKey, defaults);
            }
        }

        return client;
    }
}
