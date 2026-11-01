namespace RabstackQuery;

/// <summary>
/// Bundles a typed query key, function and options for use with
/// <see cref="QueryClient.FetchQueryAsync{TData}"/>,
/// <see cref="QueryClient.GetQueryData{TData}(QueryOptions{TData})"/> and MVVM <c>UseQuery</c> overloads.
/// Unset defaultable options inherit from key-prefix and client-wide <see cref="QueryDefaults"/>,
/// then framework defaults.
/// </summary>
public sealed class QueryOptions<TData>
{
    /// <summary>Cache identity for this query.</summary>
    public required QueryKey QueryKey { get; init; }

    /// <summary>The async function that fetches data for this query.</summary>
    public required Func<QueryFunctionContext, Task<TData>> QueryFn { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.StaleTime"/>
    public TimeSpan? StaleTime { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.GcTime"/>
    public TimeSpan? GcTime { get; init; }

    /// <summary>
    /// Number of retries after a failed fetch. <see langword="null"/> inherits from
    /// <see cref="QueryClient.SetQueryDefaults"/> and <see cref="QueryClientDefaultOptions.Queries"/>,
    /// falling back to 3 for observers and 0 for <see cref="QueryClient.FetchQueryAsync{TData}"/>.
    /// </summary>
    public int? Retry { get; init; }

    /// <inheritdoc cref="QueryObserverOptions{TData,TQueryData}.RetryDelay"/>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.NetworkMode"/>
    public NetworkMode? NetworkMode { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

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

    internal QueryObserverOptions<TData> ToObserverOptions() => new()
    {
        QueryKey = QueryKey,
        QueryFn = QueryFn,
        StaleTime = StaleTime,
        GcTime = GcTime,
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
        StaleTime = StaleTime,
        GcTime = GcTime,
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
        Retry = Retry,
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
