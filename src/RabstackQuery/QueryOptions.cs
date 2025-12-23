namespace RabstackQuery;

/// <summary>
/// Bundles a query key, query function, and common configuration into a single
/// reusable, typed object. Analogous to TanStack Query v5's <c>queryOptions()</c>
/// helper — the return value can be passed to <see cref="QueryClient.FetchQueryAsync{TData}"/>,
/// <see cref="QueryClient.GetQueryData{TData}(QueryOptions{TData})"/>, and MVVM
/// <c>UseQuery</c> overloads, with <typeparamref name="TData"/> inferred from the
/// query function.
/// <para>
/// Nullable properties mean "use default" — the consuming method fills in framework
/// defaults (matching <see cref="QueryDefaults"/> and <see cref="FetchQueryOptions{TData}"/>).
/// </para>
/// </summary>
public sealed class QueryOptions<TData>
{
    /// <summary>Cache identity for this query.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <summary>The async function that fetches data for this query.</summary>
    public required Func<QueryFunctionContext, Task<TData>> QueryFn { get; init; }

    /// <summary>
    /// Data older than this duration is considered stale. Null uses the default
    /// (<see cref="TimeSpan.Zero"/> = always stale).
    /// </summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>Garbage collection duration. Null uses the default (5 min).</summary>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Number of retry attempts for imperative fetches. Null uses the method default
    /// (0 for <see cref="QueryClient.FetchQueryAsync{TData}"/>).
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>Custom retry delay function. Receives failure count and exception.</summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public QueryMeta? Meta { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.StructuralSharing"/>
    public Func<TData, TData, TData>? StructuralSharing { get; init; }

    /// <summary>
    /// Implicit conversion to <see cref="QueryKey"/> so that a
    /// <see cref="QueryOptions{TData}"/> instance can be passed directly to
    /// filter/invalidation APIs that accept a <see cref="QueryKey"/>.
    /// </summary>
    public static implicit operator QueryKey(QueryOptions<TData> opts) => opts.QueryKey;

    /// <summary>
    /// Converts to <see cref="FetchQueryOptions{TData}"/> for imperative fetch methods.
    /// FetchQueryOptions defaults Retry to 0 (no retries), matching TanStack's fetchQuery.
    /// </summary>
    internal QueryObserverOptions<TData> ToObserverOptions() => new()
    {
        QueryKey = QueryKey,
        QueryFn = QueryFn,
        StaleTime = StaleTime ?? TimeSpan.Zero,
        CacheTime = GcTime ?? TimeSpan.FromMinutes(5),
        NetworkMode = NetworkMode,
        Retry = Retry,
        RetryDelay = RetryDelay,
        Meta = Meta,
        QueryKeyHasher = QueryKeyHasher,
        StructuralSharing = StructuralSharing,
    };

    internal QueryObserverOptions<TOut, TData> ToObserverOptions<TOut>(Func<TData, TOut> select) => new()
    {
        QueryKey = QueryKey,
        QueryFn = QueryFn,
        Select = select,
        StaleTime = StaleTime ?? TimeSpan.Zero,
        CacheTime = GcTime ?? TimeSpan.FromMinutes(5),
        NetworkMode = NetworkMode,
        Retry = Retry,
        RetryDelay = RetryDelay,
        Meta = Meta,
        QueryKeyHasher = QueryKeyHasher,
    };

    internal FetchQueryOptions<TData> ToFetchQueryOptions() => new()
    {
        QueryKey = QueryKey,
        QueryFn = QueryFn,
        StaleTime = StaleTime,
        GcTime = GcTime,
        Retry = Retry ?? 0,
        RetryDelay = RetryDelay,
        Meta = Meta,
        NetworkMode = NetworkMode,
        QueryKeyHasher = QueryKeyHasher,
    };
}

/// <summary>
/// Static factory for creating <see cref="QueryOptions{TData}"/> with type inference
/// from the query function delegate.
/// </summary>
public static class QueryOptions
{
    /// <summary>
    /// Creates a <see cref="QueryOptions{TData}"/> inferring <typeparamref name="TData"/>
    /// from the <paramref name="queryFn"/> delegate.
    /// </summary>
    public static QueryOptions<TData> Create<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        return new QueryOptions<TData> { QueryKey = queryKey, QueryFn = queryFn };
    }
}
