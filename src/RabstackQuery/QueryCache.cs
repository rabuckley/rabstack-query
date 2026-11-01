using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

public sealed class QueryCache : Subscribable<QueryCacheListener>
{
    private readonly QueryStore _queries;
    private ILogger _logger;
    private QueryMetrics? _metrics;
    private INotifyManager _notifyManager = null!;

    public QueryCache()
    {
        _queries = new QueryStore();
        _logger = NullLogger.Instance;
    }

    /// <summary>
    /// Called by <see cref="QueryClient"/> after construction to wire up the logger.
    /// QueryCache is created before QueryClient, so the logger must be set after
    /// the client is constructed.
    /// </summary>
    internal void SetLoggerFactory(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<QueryCache>();
    }

    /// <summary>
    /// Called by <see cref="QueryClient"/> after construction to wire up metrics.
    /// Same post-construction pattern as <see cref="SetLoggerFactory"/>.
    /// </summary>
    internal void SetMetrics(QueryMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Called by <see cref="QueryClient"/> after construction to wire up the
    /// per-client notification manager. Same post-construction pattern as
    /// <see cref="SetLoggerFactory"/>.
    /// </summary>
    internal void SetNotifyManager(INotifyManager notifyManager)
    {
        _notifyManager = notifyManager;
    }

    public Query<TData> GetOrCreate<TData, TQueryData>(
        QueryClient client,
        QueryConfiguration<TData> options,
        QueryState<TData>? state = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueryKey is null)
        {
            throw new ArgumentException("QueryKey must be provided.", nameof(options));
        }

        var queryKey = options.QueryKey;
        var queryHash = options.QueryHash ?? HashQueryKeyByOptions(queryKey, options);

        // TanStack's build checks then adds on one JS thread. Concurrent callers here
        // need GetOrAdd and a retry to avoid returning an uncached query.
        while (true)
        {
            if (Get<TData>(queryHash) is { } cached)
            {
                _logger.QueryCacheBuild(queryHash, createdNew: false);
                return cached;
            }

            var initialState = state;

            // Check for a hydrated placeholder that needs upgrading. Get<TData>
            // returns null for placeholders (type mismatch), so look by hash.
            var existing = GetByHash(queryHash);
            if (existing is { IsHydratedPlaceholder: true })
            {
                // Replacement is not atomic; see ARCHITECTURE.md, "Upgrade atomicity".
                var dehydrated = existing.Dehydrate(0);
                Remove(existing); // emits QueryRemoved — OK, placeholder has no observers

                initialState ??= new QueryState<TData>
                {
                    Data = dehydrated.State.Data is TData typed ? typed : default,
                    DataUpdateCount = dehydrated.State.DataUpdateCount,
                    DataUpdatedAt = dehydrated.State.DataUpdatedAt,
                    Error = dehydrated.State.Error,
                    ErrorUpdateCount = dehydrated.State.ErrorUpdateCount,
                    ErrorUpdatedAt = dehydrated.State.ErrorUpdatedAt,
                    FetchFailureCount = dehydrated.State.FetchFailureCount,
                    FetchFailureReason = dehydrated.State.FetchFailureReason,
                    FetchMeta = dehydrated.State.FetchMeta,
                    IsInvalidated = dehydrated.State.IsInvalidated,
                    Status = dehydrated.State.Status,
                    FetchStatus = FetchStatus.Idle,
                };
            }

            var query = new Query<TData>(new QueryConfig<TData>()
            {
                Client = client,
                QueryKey = queryKey,
                QueryHash = queryHash,
                Options = client.DefaultQueryOptions(options),
                State = initialState,
                Metrics = _metrics,
            });

            if (ReferenceEquals(Add(query), query))
            {
                _logger.QueryCacheBuild(queryHash, createdNew: true);
                return query;
            }

            // Even an uncached query needs its constructor's GC timer cleared.
            query.Destroy();
        }
    }

    internal Query Add(Query query)
    {
        Debug.Assert(query.QueryHash is not null);

        var cached = _queries.GetOrAdd(query.QueryHash, query);
        if (ReferenceEquals(cached, query))
        {
            _logger.QueryCacheAdd(query.QueryHash);
            _metrics?.CacheSize?.Add(1);
            Notify(new QueryCacheQueryAddedEvent { Query = query });
        }

        return cached;
    }

    internal void Remove(Query query)
    {
        Debug.Assert(query.QueryHash is not null);
        if (_queries.TryRemove(query.QueryHash, query))
        {
            _logger.QueryCacheRemove(query.QueryHash);
            _metrics?.CacheSize?.Add(-1);
            query.Destroy();
            Notify(new QueryCacheQueryRemovedEvent { Query = query });
        }
    }

    public void Clear()
    {
        _logger.QueryCacheClear();
        _notifyManager.Batch(() =>
                                     {
                                         foreach (var query in GetAll()) Remove(query);
                                     });
    }

    public Query<TData>? Get<TData>(string queryHash)
    {
        if (_queries.TryGetValue(queryHash, out var query))
        {
            if (query is Query<TData> typedQuery)
            {
                return typedQuery;
            }

            // Hydrated placeholders are Query<object> — type mismatch is expected.
            // Return null so callers (GetOrCreate, SetQueryData, GetQueryData) can fall
            // through to the create/upgrade path.
            if (query.IsHydratedPlaceholder)
            {
                return null;
            }

            throw new InvalidOperationException(
                $"Query with hash '{queryHash}' is of type '{GetFormattedName(query.GetType())}', not '{GetFormattedName(typeof(Query<TData>))}'.");
        }

        return null;
    }

    internal static string GetFormattedName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var genericArguments = type.GetGenericArguments()
            .Select(GetFormattedName)
            .Aggregate((a, b) => $"{a}, {b}");

        return $"{type.Name.AsSpan(0, type.Name.IndexOf('`'))}<{genericArguments}>";
    }

    /// <summary>
    /// Gets a query by its hash without type casting.
    /// Used internally when the query type is unknown.
    /// </summary>
    internal Query? GetByHash(string queryHash)
    {
        _queries.TryGetValue(queryHash, out var query);
        return query;
    }

    /// <summary>
    /// Read-only accessor for hydrated data that doesn't trigger placeholder
    /// upgrade. Returns <c>default</c> when the hash doesn't match a
    /// placeholder or the stored data is not of type <typeparamref name="T"/>.
    /// </summary>
    internal T? TryGetHydratedData<T>(string queryHash)
    {
        if (GetByHash(queryHash) is { IsHydratedPlaceholder: true } placeholder)
        {
            var dehydrated = placeholder.Dehydrate(0);
            return dehydrated.State.Data is T typed ? typed : default;
        }

        return default;
    }

    public IEnumerable<Query> GetAll() => _queries.Values();

    /// <summary>
    /// Returns all queries matching the given filters.
    /// </summary>
    public IEnumerable<Query> FindAll(QueryFilters? filters)
    {
        if (filters is null) return GetAll();

        var encodedFilterKey = QueryKeyMatcher.EncodeFilterKey(filters.QueryKey);
        return GetAll().Where(q => QueryKeyMatcher.MatchQuery(q, filters, encodedFilterKey));
    }

    /// <summary>Returns all queries.</summary>
    public IEnumerable<Query> FindAll() => FindAll(null);

    /// <summary>
    /// Returns the first query that exactly matches the given filters,
    /// or null if none match. Forces <c>Exact = true</c>.
    /// </summary>
    public Query? Find(QueryFilters filters)
    {
        var exactFilters = new QueryFilters
        {
            QueryKey = filters.QueryKey,
            Exact = true,
            Type = filters.Type,
            Stale = filters.Stale,
            FetchStatus = filters.FetchStatus,
            Predicate = filters.Predicate
        };

        return FindAll(exactFilters).FirstOrDefault();
    }

    private string HashQueryKeyByOptions<TData>(QueryKey queryKey, QueryConfiguration<TData> options)
    {
        var hasher = options.QueryKeyHasher ?? DefaultQueryKeyHasher.Instance;
        return hasher.HashQueryKey(queryKey);
    }


    internal void Notify(QueryCacheNotifyEvent @event)
    {
        var snapshot = GetListenerSnapshot();
        _notifyManager.Batch(() =>
        {
            foreach (var listener in snapshot) listener(@event);
        });
    }

    /// <summary>
    /// Called when the app regains focus. Each query delegates to its observers
    /// to decide whether a refetch is warranted based on their
    /// <see cref="RefetchOnBehavior"/> setting.
    /// </summary>
    public void OnFocus()
    {
        _notifyManager.Batch(() =>
                                     {
                                         foreach (var query in GetAll())
                                         {
                                             query.OnFocus();
                                         }
                                     });
    }

    /// <summary>
    /// Called when network connection is restored. Each query delegates to its
    /// observers to decide whether a refetch is warranted based on their
    /// <see cref="RefetchOnBehavior"/> setting.
    /// </summary>
    public void OnOnline()
    {
        _notifyManager.Batch(() =>
                                     {
                                         foreach (var query in GetAll())
                                         {
                                             query.OnOnline();
                                         }
                                     });
    }
}
