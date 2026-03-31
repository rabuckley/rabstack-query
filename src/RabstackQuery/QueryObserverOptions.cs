namespace RabstackQuery;

/// <summary>
/// Configuration for a <see cref="QueryObserver{TData, TQueryData}"/>, controlling
/// stale time, refetch behavior, polling, placeholder data, and data selection.
/// </summary>
public record QueryObserverOptions<TData, TQueryData>
{
    public required QueryKey QueryKey { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Dynamic enabled callback that receives the current query and returns
    /// whether the observer is enabled. When set, takes precedence over the
    /// static <see cref="Enabled"/>.
    /// <para>
    /// Mirrors TanStack's <c>enabled: (query) => boolean</c> form.
    /// The function is re-evaluated after every query state change, enabling
    /// dependent query patterns where a query starts fetching when its
    /// dependencies become available.
    /// </para>
    /// </summary>
    public Func<Query<TQueryData>, bool>? EnabledFn { get; init; }

    /// <summary>
    /// An optional selector function to transform or derive data from the query result.
    /// </summary>
    public Func<TQueryData, TData>? Select { get; init; }

    /// <summary>
    /// Duration after which data becomes stale.
    /// <see cref="TimeSpan.Zero"/> means data is always stale (default).
    /// <see cref="Timeout.InfiniteTimeSpan"/> means data is never stale, not
    /// even after invalidation — equivalent to TanStack's <c>staleTime: 'static'</c>.
    /// Ignored when <see cref="StaleTimeFn"/> is set.
    /// </summary>
    public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Dynamic stale time callback that receives the current query and returns
    /// the stale duration. Return <see cref="Timeout.InfiniteTimeSpan"/> for
    /// static behavior (never stale, not even after invalidation), equivalent to
    /// TanStack's <c>staleTime: (query) =&gt; 'static'</c>.
    /// When set, takes precedence over the static <see cref="StaleTime"/>.
    /// <para>
    /// Mirrors TanStack's <c>staleTime: (query) =&gt; number | 'static'</c> form.
    /// The function is re-evaluated after every query state change, enabling
    /// patterns where the stale duration depends on the fetched data.
    /// </para>
    /// </summary>
    public Func<Query<TQueryData>, TimeSpan>? StaleTimeFn { get; init; }

    /// <summary>
    /// Duration before inactive queries are garbage collected.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan GcTime { get; init; } = QueryTimeDefaults.GcTime;

    /// <summary>
    /// Query function to execute.
    /// </summary>
    public Func<QueryFunctionContext, Task<TQueryData>>? QueryFn { get; init; }

    /// <summary>
    /// Controls refetching when the window regains focus.
    /// Defaults to <see cref="RefetchOnBehavior.WhenStale"/> (refetch if data is stale).
    /// </summary>
    public RefetchOnBehavior RefetchOnWindowFocus { get; init; } = RefetchOnBehavior.WhenStale;

    /// <summary>
    /// Controls refetching when network connectivity is restored.
    /// Defaults to <see cref="RefetchOnBehavior.WhenStale"/> (refetch if data is stale).
    /// </summary>
    public RefetchOnBehavior RefetchOnReconnect { get; init; } = RefetchOnBehavior.WhenStale;

    /// <summary>
    /// Controls refetching when a new observer subscribes and the query already has data.
    /// Has no effect on the initial load (when no data exists — that always fetches).
    /// Defaults to <see cref="RefetchOnBehavior.WhenStale"/> (refetch if data is stale).
    /// </summary>
    public RefetchOnBehavior RefetchOnMount { get; init; } = RefetchOnBehavior.WhenStale;

    /// <summary>
    /// Interval between automatic refetches (polling).
    /// <see cref="TimeSpan.Zero"/> means polling is disabled (default).
    /// Ignored when <see cref="RefetchIntervalFn"/> is set.
    /// </summary>
    public TimeSpan RefetchInterval { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Dynamic refetch interval function that receives the current query and returns
    /// the polling interval. Return <see cref="TimeSpan.Zero"/> to disable polling,
    /// consistent with the static <see cref="RefetchInterval"/> convention.
    /// When set, takes precedence over the static <see cref="RefetchInterval"/>.
    /// <para>
    /// Mirrors TanStack's <c>refetchInterval: (query) => number | false</c> form.
    /// The function is re-evaluated after every query state change, allowing the
    /// interval to adapt based on data, error count, or other query state.
    /// </para>
    /// </summary>
    public Func<Query<TQueryData>, TimeSpan>? RefetchIntervalFn { get; init; }

    /// <summary>
    /// If true, the query will continue to refetch at the specified interval
    /// even when the application window is in the background.
    /// Defaults to false (polling pauses when unfocused).
    /// </summary>
    public bool RefetchIntervalInBackground { get; init; } = false;

    /// <summary>
    /// Provides synthetic data while the real fetch is in progress. The delegate
    /// receives the previous query's data (if any) and the previous query instance,
    /// allowing patterns like <c>keepPreviousData</c> for pagination.
    /// <para>
    /// For a static value, use <see cref="QueryUtilities.Of{T}(T)"/>. For the
    /// <c>keepPreviousData</c> pattern, use the method group
    /// <see cref="QueryUtilities.KeepPreviousData{T}(T?, Query{T}?)"/>.
    /// </para>
    /// </summary>
    public Func<TQueryData?, Query<TQueryData>?, TQueryData?>? PlaceholderData { get; init; }

    /// <summary>
    /// Number of retry attempts. Null uses the query-level default (3).
    /// </summary>
    public int? Retry { get; init; }

    /// <summary>Custom retry delay function. Receives failure count and exception.</summary>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="QueryConfiguration{TData}.Meta"/>
    public Meta? Meta { get; init; }

    /// <summary>
    /// Per-observer override for <see cref="RabstackQuery.NetworkMode"/>.
    /// When <c>null</c> (default), the query uses its own <see cref="QueryConfiguration{TData}.NetworkMode"/>.
    /// When set, overrides the query-level network mode for this observer's fetches.
    /// <para>
    /// Mirrors TanStack's <c>queryObserverOptions.networkMode</c> which inherits
    /// from the base <c>QueryOptions</c> interface and flows through to the query.
    /// </para>
    /// </summary>
    public NetworkMode? NetworkMode { get; init; }

    /// <summary>
    /// Custom query key hasher for cache identity. When <c>null</c> (default),
    /// <see cref="DefaultQueryKeyHasher.Instance"/> is used.
    /// </summary>
    public IQueryKeyHasher? QueryKeyHasher { get; init; }

    /// <summary>
    /// Explicit list of <see cref="IQueryResult{TData}"/> property names that
    /// should trigger listener notifications when their values change. Use
    /// <see cref="QueryResultProps"/> constants for type-safe property names.
    /// <para>
    /// <c>null</c> (default) = auto-tracking mode. When a
    /// <see cref="TrackedQueryResult{TData}"/> wrapper is used (e.g. via
    /// <see cref="QueriesObserver{TData}"/>'s combine function), only properties
    /// accessed through the wrapper trigger notifications. When no wrapper is used,
    /// all changes notify (backward compatible).
    /// Empty set = never notify. Set with names = notify only when at least
    /// one listed property changed.
    /// </para>
    /// <para>
    /// Mirrors TanStack's <c>notifyOnChangeProps</c> option
    /// (<c>queryObserver.ts:662–696</c>). Auto-tracking via
    /// <see cref="TrackedQueryResult{TData}"/> is the C# equivalent of TanStack's
    /// JS Proxy-based <c>trackedProps</c>.
    /// </para>
    /// </summary>
    public IReadOnlySet<string>? NotifyOnChangeProps { get; init; }

    /// <summary>
    /// Custom function to control structural sharing of query data. Called with
    /// the previous and new data after <see cref="Select"/> (or after the raw
    /// cache-to-output conversion when Select is null). Return the previous
    /// reference when deeply equal to preserve reference stability and suppress
    /// spurious change notifications. Only invoked when previous data exists.
    /// <para>
    /// <c>null</c> (default) = structural sharing disabled — new data is used
    /// as-is. Set to <see cref="RabstackQuery.StructuralSharing.ReplaceEqualDeep{T}"/>
    /// for the built-in deep-equality implementation, or provide a custom function.
    /// </para>
    /// <para>
    /// Mirrors TanStack's <c>structuralSharing</c> option (<c>types.ts:265</c>).
    /// TanStack defaults to <c>true</c>; C# defaults to <c>null</c> (disabled)
    /// because a generic property-walking implementation requires reflection,
    /// incompatible with AOT/trimming.
    /// </para>
    /// </summary>
    public Func<TData, TData, TData>? StructuralSharing { get; init; }
}

/// <summary>
/// Static factory for creating <see cref="QueryObserverOptions{TData}"/> with type inference
/// from the query function delegate.
/// </summary>
public static class QueryObserverOptions
{
    /// <summary>
    /// Creates a <see cref="QueryObserverOptions{TData}"/> inferring <typeparamref name="TData"/>
    /// from the <paramref name="queryFn"/> delegate.
    /// </summary>
    public static QueryObserverOptions<TData> Create<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        return new QueryObserverOptions<TData> { QueryKey = queryKey, QueryFn = queryFn };
    }

}
