namespace RabstackQuery.DevTools;

/// <summary>
/// Forces a query into an artificial loading state for UI development.
/// Replaces the query function with one that never completes, and sets
/// <c>Status=Pending</c>, <c>FetchStatus=Fetching</c>, <c>Data=default</c>.
/// Mirrors TanStack devtools' <c>triggerLoading</c>.
/// </summary>
internal sealed class TriggerLoadingOperation : IQueryOperation<ValueTuple>
{
    public ValueTuple Execute<TData>(Query<TData> query)
    {
        var savedState = query.State;
        if (savedState is null) return default;

        var savedQueryFn = query.QueryFn;

        // Preserve existing FetchMeta fields (e.g. FetchMore for infinite queries)
        // while storing restoration data.
        var fetchMeta = new FetchMeta
        {
            FetchMore = savedState.FetchMeta?.FetchMore,
            PreviousQueryFn = savedQueryFn,
            PreviousState = savedState,
        };

        query.Cancel(new CancelOptions { Silent = true });

        // Replace queryFn with one that never completes, then start an actual fetch.
        // Starting a real fetch creates a Retryer and sets _activeFetch, which
        // prevents automatic refetches (polling, window focus) from clobbering our
        // artificial state — they hit the deduplication path instead.
        //
        // SetQueryFn + Fetch are not atomic at the API level but safe here — both
        // run inside Execute<TData> with no possible interleaving from external
        // observers. The Fetch dispatches FetchAction synchronously (setting
        // FetchMeta=null and FetchStatus=Fetching) before returning. Our SetState
        // below then overwrites with the desired state including our FetchMeta.
        query.SetQueryFn(_ => new TaskCompletionSource<TData>().Task);
        _ = query.Fetch();

        query.SetState(new QueryState<TData>
        {
            Data = default,
            DataUpdateCount = savedState.DataUpdateCount,
            DataUpdatedAt = savedState.DataUpdatedAt,
            Error = null,
            ErrorUpdateCount = savedState.ErrorUpdateCount,
            ErrorUpdatedAt = savedState.ErrorUpdatedAt,
            FetchFailureCount = 0,
            FetchFailureReason = null,
            FetchMeta = fetchMeta,
            IsInvalidated = false,
            Status = QueryStatus.Pending,
            FetchStatus = FetchStatus.Fetching,
        });

        return default;
    }
}
