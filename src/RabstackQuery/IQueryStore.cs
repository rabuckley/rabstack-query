namespace RabstackQuery;

internal interface IQueryStore
{
    public bool Has(string queryHash);

    public bool Set(string queryHash, Query query);

    public Query? Get(string queryHash);

    public void Delete(string queryHash);
}
