namespace RabstackQuery;

/// <summary>
/// Interface for query observers to enable polymorphic notifications across different TData/TQueryData types.
/// </summary>
internal interface IQueryObserver
{
    void OnQueryUpdate(DispatchAction action);
    bool ShouldFetchOnWindowFocus();
    bool ShouldFetchOnReconnect();

    /// <summary>
    /// Whether this observer is enabled. Resolves the static Enabled flag or
    /// the dynamic EnabledFn callback. Used by <see cref="Query{TData}.IsDisabled"/>
    /// to determine whether the query has any active observers, matching TanStack's
    /// <c>resolveEnabled(observer.options.enabled, this) !== false</c> check.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether this observer's resolved stale time is "static" (never stale).
    /// Used by <see cref="Query{TData}.IsStatic"/> to determine whether the
    /// query should be skipped during <c>RefetchQueriesAsync</c>, matching TanStack's
    /// <c>resolveStaleTime(observer.options.staleTime, this) === 'static'</c>.
    /// </summary>
    bool IsStaleTimeStatic { get; }

    /// <summary>
    /// Whether this observer's current result considers the data stale.
    /// Used by <see cref="Query{TData}.IsStale"/> to delegate staleness to
    /// observers when they exist, matching TanStack's
    /// <c>this.observers.some(o => o.getCurrentResult().isStale)</c>.
    /// </summary>
    bool IsCurrentResultStale { get; }
}
