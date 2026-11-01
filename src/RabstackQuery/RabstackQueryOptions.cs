using Microsoft.Extensions.DependencyInjection;

namespace RabstackQuery;

/// <summary>
/// Configuration for <see cref="ServiceCollectionExtensions.AddRabstackQuery"/>.
/// Populated by the configuration delegate and applied to the <see cref="QueryClient"/>
/// after construction.
/// </summary>
public sealed class RabstackQueryOptions
{
    /// <summary>
    /// Client-wide default options, assigned to <see cref="QueryClient.DefaultOptions"/>.
    /// </summary>
    public QueryClientDefaultOptions? DefaultOptions { get; set; }

    /// <summary>
    /// Cache-level mutation callbacks (OnMutate, OnSuccess, OnError, OnSettled).
    /// Passed to the <see cref="MutationCache"/> constructor.
    /// </summary>
    public MutationCacheConfig? MutationCacheConfig { get; set; }

    internal List<(QueryKey Key, QueryDefaults Defaults)> QueryDefaults { get; } = [];
    internal List<(QueryKey Key, MutationDefaults Defaults)> MutationDefaults { get; } = [];

    /// <summary>
    /// Registers defaults for every query whose key starts with <paramref name="queryKey"/>.
    /// Applied with <see cref="QueryClient.SetQueryDefaults"/> in registration order.
    /// </summary>
    /// <param name="queryKey">The key prefix that query keys are matched against.</param>
    /// <param name="defaults">The defaults to apply to matching queries.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="queryKey"/> or <paramref name="defaults"/> is <see langword="null"/>.
    /// </exception>
    public void SetQueryDefaults(QueryKey queryKey, QueryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        ArgumentNullException.ThrowIfNull(defaults);

        QueryDefaults.Add((queryKey, defaults));
    }

    /// <summary>
    /// Registers defaults for every mutation whose key starts with <paramref name="mutationKey"/>.
    /// Applied with <see cref="QueryClient.SetMutationDefaults"/> in registration order.
    /// </summary>
    /// <param name="mutationKey">The key prefix that mutation keys are matched against.</param>
    /// <param name="defaults">The defaults to apply to matching mutations.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mutationKey"/> or <paramref name="defaults"/> is <see langword="null"/>.
    /// </exception>
    public void SetMutationDefaults(QueryKey mutationKey, MutationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(mutationKey);
        ArgumentNullException.ThrowIfNull(defaults);

        MutationDefaults.Add((mutationKey, defaults));
    }
}
