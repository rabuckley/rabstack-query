namespace RabstackQuery;

internal sealed class DefaultQueryKeyHasher : IQueryKeyHasher
{
    /// <summary>
    /// Shared instance. The hasher is stateless, so there's no need to create one per call.
    /// </summary>
    public static readonly DefaultQueryKeyHasher Instance = new();

    public string HashQueryKey(QueryKey queryKey) => QueryKeyEncoder.Encode(queryKey);
}
