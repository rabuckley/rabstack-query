namespace RabstackQuery.DevTools;

/// <summary>
/// Forces a query into an artificial error state for UI development.
/// Does NOT replace the query function — the original stays so that
/// a natural refetch can recover. Sets <c>Status=Errored</c>,
/// <c>FetchStatus=Idle</c>, <c>Data=default</c>.
/// Mirrors TanStack devtools' <c>triggerError</c>.
/// </summary>
internal sealed class TriggerErrorOperation : IQueryOperation<ValueTuple>
{
    private readonly Exception _error;

    public TriggerErrorOperation(Exception error)
    {
        _error = error;
    }

    public ValueTuple Execute<TData>(Query<TData> query)
    {
        var savedState = query.State;
        if (savedState is null) return default;

        var savedQueryFn = query.QueryFn;

        // Preserve existing FetchMeta fields while storing restoration data.
        // QueryFn is saved for uniform restore even though it isn't replaced.
        var fetchMeta = new FetchMeta
        {
            FetchMore = savedState.FetchMeta?.FetchMore,
            PreviousQueryFn = savedQueryFn,
            PreviousState = savedState,
        };

        query.Cancel(new CancelOptions { Silent = true });

        query.SetState(new QueryState<TData>
        {
            Data = default,
            DataUpdateCount = savedState.DataUpdateCount,
            DataUpdatedAt = savedState.DataUpdatedAt,
            Error = _error,
            ErrorUpdateCount = savedState.ErrorUpdateCount + 1,
            ErrorUpdatedAt = savedState.ErrorUpdatedAt,
            FetchFailureCount = savedState.FetchFailureCount + 1,
            FetchFailureReason = _error,
            FetchMeta = fetchMeta,
            IsInvalidated = true,
            Status = QueryStatus.Errored,
            FetchStatus = FetchStatus.Idle,
        });

        return default;
    }
}
