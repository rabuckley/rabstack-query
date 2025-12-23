namespace RabstackQuery.DevTools;

/// <summary>
/// Subscribes to <see cref="QueryCache"/> events and builds debounced snapshots
/// of all queries and mutations for display. Events are debounced to at most one
/// rebuild per 250ms to avoid excessive UI updates during batch operations.
/// </summary>
public sealed class CacheObserver : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);

    private readonly QueryClient _queryClient;
    private readonly Func<object?, string> _dataFormatter;
    private readonly SynchronizationContext? _syncContext;
    private readonly IDisposable _cacheSubscription;
    private readonly IDisposable _mutationCacheSubscription;
    private readonly ITimer _debounceTimer;

    private List<QueryListItem> _queries = [];
    private List<MutationListItem> _mutations = [];

    /// <summary>
    /// Raised on the UI thread (when a <see cref="SynchronizationContext"/> was
    /// captured at construction) after snapshots are rebuilt.
    /// </summary>
    public event Action? SnapshotsChanged;

    public IReadOnlyList<QueryListItem> Queries => _queries;
    public IReadOnlyList<MutationListItem> Mutations => _mutations;
    public int QueryCount => _queries.Count;

    public CacheObserver(QueryClient queryClient, DevToolsOptions options)
    {
        _queryClient = queryClient;
        _dataFormatter = options.DataFormatter ?? (data => data?.ToString() ?? "(null)");

        // Capture the UI sync context so snapshot-changed notifications
        // marshal to the main thread — same pattern as QueryViewModel.
        _syncContext = SynchronizationContext.Current;

        _debounceTimer = queryClient.TimeProvider.CreateTimer(
            OnDebounceExpired, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        var queryCache = queryClient.GetQueryCache();
        _cacheSubscription = queryCache.Subscribe(OnCacheEvent);

        var mutationCache = queryClient.GetMutationCache();
        _mutationCacheSubscription = mutationCache.Subscribe(OnMutationCacheEvent);

        // Initial snapshot so the FAB badge shows the correct count immediately.
        RebuildSnapshots();
    }

    /// <summary>
    /// Forces an immediate rebuild of all snapshots, bypassing the debounce timer.
    /// </summary>
    public void ForceRefresh()
    {
        RebuildSnapshots();
    }

    /// <summary>
    /// Looks up a live <see cref="Query"/> by its hash so UI projects can execute
    /// actions (refetch, invalidate, reset, remove) without holding a direct
    /// reference on the snapshot record.
    /// </summary>
    public Query? FindQueryByHash(string queryHash) =>
        _queryClient.GetQueryCache().GetAll()
            .FirstOrDefault(q => q.QueryHash == queryHash);

    private void OnCacheEvent(QueryCacheNotifyEvent _)
    {
        _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
    }

    private void OnMutationCacheEvent(MutationCacheNotifyEvent _)
    {
        _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
    }

    private void OnDebounceExpired(object? _)
    {
        RebuildSnapshots();
    }

    private void RebuildSnapshots()
    {
        var queryCache = _queryClient.GetQueryCache();
        var mutationCache = _queryClient.GetMutationCache();

        // Queries — Dehydrate() is internal, accessible via InternalsVisibleTo.
        var queries = new List<QueryListItem>();
        foreach (var query in queryCache.GetAll())
        {
            var dehydrated = query.Dehydrate(0);
            var displayStatus = DetermineDisplayStatus(query);

            queries.Add(new QueryListItem
            {
                QueryHash = query.QueryHash ?? "",
                QueryKeyDisplay = FormatQueryKey(query.QueryKey),
                DisplayStatus = displayStatus,
                Status = query.CurrentStatus,
                FetchStatus = query.CurrentFetchStatus,
                ObserverCount = query.ObserverCount,
                IsStale = query.IsStale(),
                IsDisabled = query.IsDisabled(),
                DataUpdatedAt = dehydrated.State.DataUpdatedAt,
                DataDisplay = _dataFormatter(dehydrated.State.Data),
                IsInvalidated = dehydrated.State.IsInvalidated,
                FetchFailureCount = dehydrated.State.FetchFailureCount,
                ErrorDisplay = dehydrated.State.Error?.ToString(),
            });
        }

        // Mutations — Dehydrate() gives access to generic-typed data/variables.
        var mutations = new List<MutationListItem>();
        foreach (var mutation in mutationCache.GetAll())
        {
            var dehydrated = mutation.Dehydrate();
            mutations.Add(new MutationListItem
            {
                MutationId = mutation.MutationId,
                MutationKeyDisplay = FormatQueryKey(mutation.MutationKey),
                Status = mutation.CurrentStatus,
                IsPaused = mutation.CurrentIsPaused,
                HasObservers = mutation.HasObservers,
                DataDisplay = _dataFormatter(dehydrated.State.Data),
                VariablesDisplay = _dataFormatter(dehydrated.State.Variables),
                SubmittedAt = dehydrated.State.SubmittedAt,
                ErrorDisplay = dehydrated.State.Error?.ToString(),
                FailureCount = dehydrated.State.FailureCount,
            });
        }

        _queries = queries;
        _mutations = mutations;

        if (_syncContext is { } ctx)
        {
            ctx.Post(_ => SnapshotsChanged?.Invoke(), null);
        }
        else
        {
            SnapshotsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Maps raw query state into a single display status for color-coding.
    /// Priority order matches TanStack React Query DevTools.
    /// </summary>
    private static QueryDisplayStatus DetermineDisplayStatus(Query query)
    {
        if (query.CurrentFetchStatus is FetchStatus.Fetching) return QueryDisplayStatus.Fetching;
        if (query.CurrentFetchStatus is FetchStatus.Paused) return QueryDisplayStatus.Paused;
        if (query.CurrentStatus is QueryStatus.Errored) return QueryDisplayStatus.Error;
        if (query.ObserverCount == 0) return QueryDisplayStatus.Inactive;
        if (query.IsStale()) return QueryDisplayStatus.Stale;
        return QueryDisplayStatus.Fresh;
    }

    private static string FormatQueryKey(QueryKey? key)
    {
        if (key is null) return "(none)";
        return $"[{string.Join(", ", key.Select(k => k?.ToString() ?? "null"))}]";
    }

    public void Dispose()
    {
        _cacheSubscription.Dispose();
        _mutationCacheSubscription.Dispose();
        _debounceTimer.Dispose();
    }
}
