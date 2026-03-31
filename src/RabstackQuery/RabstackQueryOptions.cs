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
    /// Global default options applied to all queries via
    /// <see cref="QueryClient.DefaultOptions"/>.
    /// </summary>
    public QueryClientDefaultOptions? DefaultOptions { get; set; }

    /// <summary>
    /// Cache-level mutation callbacks (OnMutate, OnSuccess, OnError, OnSettled).
    /// Passed to the <see cref="MutationCache"/> constructor.
    /// </summary>
    public MutationCacheConfig? MutationCacheConfig { get; set; }

    internal List<QueryDefaults>? QueryDefaults { get; private set; }
    internal List<MutationDefaults>? MutationDefaults { get; private set; }

    /// <summary>
    /// Registers per-key-prefix defaults for queries. Any query whose key starts
    /// with the key in <paramref name="defaults"/> inherits these defaults (unless
    /// overridden by per-query options). Mirrors <see cref="QueryClient.SetQueryDefaults"/>.
    /// </summary>
    /// <param name="defaults">The defaults to register, including the key prefix they apply to.</param>
    public void SetQueryDefaults(QueryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        QueryDefaults ??= [];
        QueryDefaults.Add(defaults);
    }

    /// <summary>
    /// Registers per-key-prefix defaults for mutations. Any mutation whose key
    /// starts with the key in <paramref name="defaults"/> inherits these defaults.
    /// Mirrors <see cref="QueryClient.SetMutationDefaults"/>.
    /// </summary>
    /// <param name="defaults">The defaults to register, including the mutation key prefix they apply to.</param>
    public void SetMutationDefaults(MutationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        MutationDefaults ??= [];
        MutationDefaults.Add(defaults);
    }
}
