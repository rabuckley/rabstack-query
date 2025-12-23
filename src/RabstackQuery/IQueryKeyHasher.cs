namespace RabstackQuery;

public interface IQueryKeyHasher
{
    string HashQueryKey(QueryKey queryKey);
}
