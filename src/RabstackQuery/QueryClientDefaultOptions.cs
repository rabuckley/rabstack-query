namespace RabstackQuery;

public sealed class QueryClientDefaultOptions
{
    /// <summary>
    /// Gets the defaults applied to every query. These form the lowest-precedence layer:
    /// key-prefix defaults registered with <see cref="QueryClient.SetQueryDefaults"/> and
    /// per-query options override them.
    /// </summary>
    public QueryDefaults? Queries { get; init; }

    /// <summary>
    /// Gets the defaults applied to every mutation. These form the lowest-precedence layer:
    /// key-prefix defaults registered with <see cref="QueryClient.SetMutationDefaults"/> and
    /// per-mutation options override them.
    /// </summary>
    public MutationDefaults? Mutations { get; init; }

    /// <summary>
    /// Default options for <see cref="QueryClient.Dehydrate"/>.
    /// </summary>
    public DehydrateOptions? Dehydrate { get; init; }

    /// <summary>
    /// Default options for <see cref="QueryClient.Hydrate"/>.
    /// </summary>
    public HydrateOptions? Hydrate { get; init; }
}
