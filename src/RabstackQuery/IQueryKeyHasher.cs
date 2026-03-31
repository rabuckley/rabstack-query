namespace RabstackQuery;

/// <summary>
/// Produces a deterministic hash string from a <see cref="QueryKey"/> for use as
/// the cache identity in <see cref="QueryCache"/>.
/// </summary>
public interface IQueryKeyHasher
{
    string HashQueryKey(QueryKey queryKey);
}
