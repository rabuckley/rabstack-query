using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery;

internal sealed class QueryStore : IQueryStore
{
    private readonly ConcurrentDictionary<string, Query> _cache = new();

    public void Delete(string queryHash) => _ = _cache.TryRemove(queryHash, out _);

    public Query? Get(string queryHash) => _cache.GetValueOrDefault(queryHash);

    public bool Has(string queryHash) => _cache.ContainsKey(queryHash);

    public bool Set(string queryHash, Query query) => _cache.TryAdd(queryHash, query);

    public bool TryAdd(string queryHash, Query query) => _cache.TryAdd(queryHash, query);

    public bool TryRemove(string queryHash, [NotNullWhen(true)] out Query? query)
    {
        return _cache.TryRemove(queryHash, out query);
    }

    public bool TryGetValue(string queryHash, [NotNullWhen(true)] out Query? query)
    {
        return _cache.TryGetValue(queryHash, out query);
    }

    public IEnumerable<Query> Values() => _cache.Values;
}
