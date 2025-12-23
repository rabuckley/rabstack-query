namespace RabstackQuery;

/// <summary>
/// Context passed to mutation functions and lifecycle callbacks.
/// Provides access to QueryClient, metadata, and mutation key.
/// </summary>
public sealed class MutationFunctionContext
{
    /// <summary>
    /// The QueryClient instance managing this mutation.
    /// </summary>
    public QueryClient Client { get; }

    /// <summary>
    /// Optional metadata associated with this mutation.
    /// </summary>
    public MutationMeta? Meta { get; }

    /// <summary>
    /// Optional mutation key for identifying this mutation.
    /// </summary>
    public QueryKey? MutationKey { get; }

    /// <summary>
    /// Creates a new MutationFunctionContext.
    /// </summary>
    internal MutationFunctionContext(
        QueryClient client,
        MutationMeta? meta,
        QueryKey? mutationKey)
    {
        Client = client;
        Meta = meta;
        MutationKey = mutationKey;
    }
}
