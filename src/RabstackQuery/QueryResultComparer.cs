namespace RabstackQuery;

/// <summary>
/// Per-property change detection for <see cref="IQueryResult{TData}"/>.
/// Used by <see cref="QueryObserver{TData,TQueryData}.ShouldNotifyListeners"/>
/// to determine whether any tracked property has changed between two results.
/// </summary>
internal static class QueryResultComparer
{
    /// <summary>
    /// Returns <c>true</c> if any property named in <paramref name="props"/> differs
    /// between <paramref name="prev"/> and <paramref name="curr"/>.
    /// </summary>
    public static bool HasChangedProperty<TData>(
        IQueryResult<TData> prev,
        IQueryResult<TData> curr,
        IReadOnlySet<string> props)
    {
        foreach (var prop in props)
        {
            var changed = prop switch
            {
                QueryResultProps.Data => !EqualityComparer<TData?>.Default.Equals(prev.Data, curr.Data),
                QueryResultProps.DataUpdatedAt => prev.DataUpdatedAt != curr.DataUpdatedAt,
                QueryResultProps.Error => !ReferenceEquals(prev.Error, curr.Error),
                QueryResultProps.ErrorUpdatedAt => prev.ErrorUpdatedAt != curr.ErrorUpdatedAt,
                QueryResultProps.FailureCount => prev.FailureCount != curr.FailureCount,
                QueryResultProps.FailureReason => !ReferenceEquals(prev.FailureReason, curr.FailureReason),
                QueryResultProps.ErrorUpdateCount => prev.ErrorUpdateCount != curr.ErrorUpdateCount,
                QueryResultProps.IsError => prev.IsError != curr.IsError,
                QueryResultProps.IsFetched => prev.IsFetched != curr.IsFetched,
                QueryResultProps.IsFetchedAfterMount => prev.IsFetchedAfterMount != curr.IsFetchedAfterMount,
                QueryResultProps.IsFetching => prev.IsFetching != curr.IsFetching,
                QueryResultProps.IsLoading => prev.IsLoading != curr.IsLoading,
                QueryResultProps.IsPending => prev.IsPending != curr.IsPending,
                QueryResultProps.IsLoadingError => prev.IsLoadingError != curr.IsLoadingError,
                QueryResultProps.IsPaused => prev.IsPaused != curr.IsPaused,
                QueryResultProps.IsPlaceholderData => prev.IsPlaceholderData != curr.IsPlaceholderData,
                QueryResultProps.IsRefetchError => prev.IsRefetchError != curr.IsRefetchError,
                QueryResultProps.IsRefetching => prev.IsRefetching != curr.IsRefetching,
                QueryResultProps.IsStale => prev.IsStale != curr.IsStale,
                QueryResultProps.IsSuccess => prev.IsSuccess != curr.IsSuccess,
                QueryResultProps.IsEnabled => prev.IsEnabled != curr.IsEnabled,
                QueryResultProps.Status => prev.Status != curr.Status,
                QueryResultProps.FetchStatus => prev.FetchStatus != curr.FetchStatus,
                _ => true // Unknown property name — notify to be safe
            };

            if (changed) return true;
        }

        return false;
    }
}
