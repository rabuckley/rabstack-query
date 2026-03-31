namespace RabstackQuery.DevTools;

/// <summary>
/// Restores a query from an artificial loading/error state back to its
/// pre-trigger state, then re-fetches with the original query function.
/// Mirrors TanStack devtools' <c>restoreQueryAfterLoadingOrError</c>.
/// </summary>
internal sealed class RestoreOperation : IQueryOperation<Task>
{
    public async Task Execute<TData>(Query<TData> query)
    {
        var currentState = query.State;
        if (currentState?.FetchMeta?.PreviousState is not QueryState<TData> savedState)
            return;

        query.Cancel(new CancelOptions { Silent = true });

        // Restore the original query function if it was replaced (trigger-loading).
        if (currentState.FetchMeta.PreviousQueryFn is Func<QueryFunctionContext, Task<TData>> savedQueryFn)
            query.SetQueryFn(savedQueryFn);

        // Restore pre-trigger state with FetchStatus=Idle and FetchMeta cleared
        // (preserving FetchMore if it was set before the trigger).
        query.SetState(new QueryState<TData>
        {
            Data = savedState.Data,
            DataUpdateCount = savedState.DataUpdateCount,
            DataUpdatedAt = savedState.DataUpdatedAt,
            Error = savedState.Error,
            ErrorUpdateCount = savedState.ErrorUpdateCount,
            ErrorUpdatedAt = savedState.ErrorUpdatedAt,
            FetchFailureCount = savedState.FetchFailureCount,
            FetchFailureReason = savedState.FetchFailureReason,
            FetchMeta = savedState.FetchMeta?.FetchMore is not null
                ? new FetchMeta { FetchMore = savedState.FetchMeta.FetchMore }
                : null,
            IsInvalidated = savedState.IsInvalidated,
            Status = savedState.Status,
            FetchStatus = FetchStatus.Idle,
        });

        // Re-fetch with the restored (or original) query function.
        try { await query.Fetch(); }
        catch { /* Query errors are tracked in query state, not surfaced here */ }
    }
}
