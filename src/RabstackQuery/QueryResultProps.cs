namespace RabstackQuery;

/// <summary>
/// Property name constants for <see cref="IQueryResult{TData}"/>. Used with
/// <see cref="QueryObserverOptions{TData,TQueryData}.NotifyOnChangeProps"/> to
/// specify which property changes should trigger listener notifications.
/// </summary>
public static class QueryResultProps
{
    public const string Data = nameof(IQueryResult<object>.Data);
    public const string DataUpdatedAt = nameof(IQueryResult<object>.DataUpdatedAt);
    public const string Error = nameof(IQueryResult<object>.Error);
    public const string ErrorUpdatedAt = nameof(IQueryResult<object>.ErrorUpdatedAt);
    public const string FailureCount = nameof(IQueryResult<object>.FailureCount);
    public const string FailureReason = nameof(IQueryResult<object>.FailureReason);
    public const string ErrorUpdateCount = nameof(IQueryResult<object>.ErrorUpdateCount);
    public const string IsError = nameof(IQueryResult<object>.IsError);
    public const string IsFetched = nameof(IQueryResult<object>.IsFetched);
    public const string IsFetchedAfterMount = nameof(IQueryResult<object>.IsFetchedAfterMount);
    public const string IsFetching = nameof(IQueryResult<object>.IsFetching);
    public const string IsLoading = nameof(IQueryResult<object>.IsLoading);
    public const string IsPending = nameof(IQueryResult<object>.IsPending);
    public const string IsLoadingError = nameof(IQueryResult<object>.IsLoadingError);
    public const string IsPaused = nameof(IQueryResult<object>.IsPaused);
    public const string IsPlaceholderData = nameof(IQueryResult<object>.IsPlaceholderData);
    public const string IsRefetchError = nameof(IQueryResult<object>.IsRefetchError);
    public const string IsRefetching = nameof(IQueryResult<object>.IsRefetching);
    public const string IsStale = nameof(IQueryResult<object>.IsStale);
    public const string IsSuccess = nameof(IQueryResult<object>.IsSuccess);
    public const string IsEnabled = nameof(IQueryResult<object>.IsEnabled);
    public const string Status = nameof(IQueryResult<object>.Status);
    public const string FetchStatus = nameof(IQueryResult<object>.FetchStatus);
}
