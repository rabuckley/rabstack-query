namespace RabstackQuery;

public static class QueryKeyCollectionBuilder
{
    public static QueryKey Create(ReadOnlySpan<object> span)
    {
        return QueryKey.CreateUnsafe([.. span]);
    }
}
