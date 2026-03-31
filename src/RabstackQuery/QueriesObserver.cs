namespace RabstackQuery;

/// <summary>
/// Manages multiple <see cref="QueryObserver{TData,TQueryData}"/> instances as a group,
/// providing a unified notification stream when any individual query changes state.
///
/// <para>
/// Useful for dashboards and any scenario where several independent queries must be
/// tracked together — e.g., pre-fetching a page of items or combining data from
/// multiple endpoints into a single result object.
/// </para>
///
/// <para>
/// <b>Homogeneous type constraint:</b> all queries in the group must share the same
/// <typeparamref name="TData"/> type. For queries that return different raw types,
/// use per-observer <c>Select</c> transforms to normalise them to a common shape
/// before constructing the group.
/// </para>
///
/// <para>
/// <b>Observer reuse:</b> <see cref="SetQueries(IReadOnlyList{QueryObserverOptions{TData,TData}})"/>
/// reuses existing observers when the new query list contains a matching query key hash.
/// This avoids unnecessary subscription teardown and re-fetch. When a key moves position
/// the observer moves with it; when it disappears entirely the observer is destroyed.
/// </para>
///
/// <para>
/// <b>Combine memoization:</b> when a <c>combine</c> function is configured via
/// <see cref="SetQueries(IReadOnlyList{QueryObserverOptions{TData,TData}}, Func{IReadOnlyList{IQueryResult{TData}}, TCombinedResult}?)"/>,
/// the combined result is memoized. Outer listeners are suppressed when the combined
/// output hasn't changed (by reference equality). Mirrors TanStack's
/// <c>#combineResult</c> / <c>#notify</c> in <c>queriesObserver.ts:172–309</c>.
/// </para>
///
/// <para>
/// <b>Auto-tracking:</b> when a combine function is present and an observer's
/// <see cref="QueryObserverOptions{TData,TQueryData}.NotifyOnChangeProps"/> is null,
/// results are wrapped in <see cref="TrackedQueryResult{TData}"/> before being passed
/// to combine. Property accesses inside combine are recorded on each observer so that
/// subsequent notifications are suppressed when only untracked properties change.
/// Mirrors TanStack's <c>#trackResult</c> (<c>queriesObserver.ts:295–309</c>, PR #7000).
/// </para>
/// </summary>
/// <remarks>
/// The only intended subclass is <see cref="QueriesObserver{TData}"/>, a convenience
/// alias that defaults <typeparamref name="TCombinedResult"/>. Do not subclass externally.
/// </remarks>
public class QueriesObserver<TData, TCombinedResult> : Subscribable<QueriesObserverListener<TData>>
{
    private readonly QueryClient _client;

    // Active observers, one per entry in the query list passed to SetQueries.
    // Kept in sync with the query options list by SetQueries.
    private List<QueryObserver<TData, TData>> _observers = [];

    // The last snapshot of results, one per observer. Replaced (not mutated) on
    // each update so listeners holding a reference to a previous snapshot see a
    // stable, immutable view.
    private IReadOnlyList<IQueryResult<TData>> _result = [];

    // Subscription handles for the inner observer callbacks. Only populated while
    // this QueriesObserver has at least one outer listener. Keyed by observer
    // reference (identity equality) so lookups survive hash collisions between
    // different observers for the same query key.
    private readonly Dictionary<QueryObserver<TData, TData>, IDisposable> _innerSubscriptions
        = new(ReferenceEqualityComparer.Instance);

    // Guards _observers, _result, _observerMatches, _innerSubscriptions,
    // _combine, and the combine memoization fields. TanStack is single-threaded
    // so doesn't need this.
    //
    // Lock ordering: _listenersLock → _stateLock (never the reverse).
    // OnSubscribe/OnUnsubscribe run under _listenersLock and acquire _stateLock
    // for _innerSubscriptions and _observers access.
    private readonly Lock _stateLock = new();

    // ── Combine memoization state ──────────────────────────────────────────────
    // Mirrors TanStack's `#lastCombine`, `#lastResult`, `#lastQueryHashes`,
    // `#combinedResult` in queriesObserver.ts:43–47.

    private Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? _combine;
    private TCombinedResult? _combinedResult;
    private bool _hasCombinedResult;
    private IReadOnlyList<IQueryResult<TData>>? _lastCombineInput;
    private List<string>? _lastQueryHashes;
    private Delegate? _lastCombine;

    // Observer matches from the last SetQueries call, used for trackResult.
    private List<ObserverMatch> _observerMatches = [];

    /// <param name="client">The <see cref="QueryClient"/> that owns the query cache.</param>
    /// <param name="queries">
    /// The initial set of query option objects. Each entry maps to one
    /// <see cref="QueryObserver{TData,TQueryData}"/> under the hood.
    /// </param>
    /// <param name="combine">
    /// Optional function to combine individual query results into a single value.
    /// When set, outer notifications are suppressed when the combined result
    /// hasn't changed (by reference equality).
    /// </param>
    public QueriesObserver(
        QueryClient client,
        IReadOnlyList<QueryObserverOptions<TData, TData>> queries,
        Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? combine = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(queries);

        _client = client;
        _combine = combine;

        // Initialise _observers and _result without subscribing to inner observers.
        // Inner subscriptions are created lazily in OnSubscribe when the first outer
        // listener arrives, matching TanStack's onSubscribe behaviour: the inner
        // observers only start fetching when someone actually cares about the results.
        SetQueries(queries);
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current snapshot of query results, one per query in the group.
    /// The list is replaced (never mutated) on each update; callers may safely
    /// retain references to previous snapshots.
    /// </summary>
    /// <remarks>
    /// Thread safety: the reference read is atomic, but this property is not
    /// synchronized with <see cref="GetObservers"/>. A caller reading both
    /// from a background thread may see them from different logical states.
    /// This matches TanStack's semantics (no atomic "get both" API exists there
    /// either). Consistent snapshots are only guaranteed within
    /// <see cref="Subscribable{TListener}.Subscribe"/> callbacks.
    /// </remarks>
    public IReadOnlyList<IQueryResult<TData>> CurrentResult => _result;

    /// <summary>
    /// Returns the underlying observer list in the same order as the current query list.
    /// Intended for diagnostics and advanced composition scenarios.
    /// </summary>
    /// <remarks>
    /// Thread safety: see <see cref="CurrentResult"/> remarks.
    /// </remarks>
    public IReadOnlyList<QueryObserver<TData, TData>> GetObservers() => _observers;

    /// <summary>
    /// Tears down all inner observer subscriptions. After calling this method,
    /// the <see cref="QueriesObserver{TData,TCombinedResult}"/> no longer tracks any queries
    /// and no further notifications will be emitted to listeners.
    /// Mirrors TanStack's <c>QueriesObserver.destroy()</c>.
    /// </summary>
    public void Destroy()
    {
        // Snapshot and clear under _stateLock, dispose outside to avoid holding
        // the lock during external OnUnsubscribe callbacks.
        IDisposable[] subscriptions;
        lock (_stateLock)
        {
            subscriptions = [.. _innerSubscriptions.Values];
            _innerSubscriptions.Clear();
            _observers = [];
            _result = [];
            _observerMatches = [];
            _combinedResult = default;
            _hasCombinedResult = false;
            _lastCombineInput = null;
            _lastQueryHashes = null;
            _lastCombine = null;
        }

        foreach (var sub in subscriptions)
            sub.Dispose();
    }

    /// <summary>
    /// Replaces the observed query list, preserving the current combine function.
    /// </summary>
    public void SetQueries(IReadOnlyList<QueryObserverOptions<TData, TData>> queries)
    {
        Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? combine;
        lock (_stateLock) { combine = _combine; }
        SetQueries(queries, combine);
    }

    /// <summary>
    /// Replaces the observed query list and optionally updates the combine function.
    /// Observers for query keys that still appear in the new list are reused (their
    /// options are updated via <see cref="QueryObserver{TData,TQueryData}.SetOptions"/>).
    /// Observers for keys that no longer appear are destroyed. New observers are created
    /// for newly added keys.
    ///
    /// <para>
    /// When <see cref="Subscribable{TListener}.HasListeners"/> is true, subscription
    /// management for added/removed inner observers happens synchronously inside a
    /// <see cref="NotifyManager"/> batch so that outer listeners receive a single,
    /// consistent notification reflecting all structural changes at once.
    /// </para>
    /// </summary>
    public void SetQueries(
        IReadOnlyList<QueryObserverOptions<TData, TData>> queries,
        Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? combine)
    {
        ArgumentNullException.ThrowIfNull(queries);
        lock (_stateLock) { _combine = combine; }

        _client.NotifyManager.Batch(() =>
        {
            List<QueryObserver<TData, TData>> prevObservers;
            IReadOnlyList<IQueryResult<TData>> currentResult;
            lock (_stateLock)
            {
                prevObservers = _observers;
                currentResult = _result;
            }

            var newMatches = FindMatchingObservers(queries, prevObservers);
            var newObservers = newMatches.Select(m => m.Observer).ToList();
            var newResults = newObservers.Select(obs => obs.CurrentResult).ToList();

            // ── Detect structural change (observer identity or order changed) ────
            var hasLengthChange = prevObservers.Count != newObservers.Count;
            var hasIndexChange = !hasLengthChange && newObservers
                .Select((obs, i) => !ReferenceEquals(obs, prevObservers[i]))
                .Any(changed => changed);
            var hasStructuralChange = hasLengthChange || hasIndexChange;

            // ── Detect result change (any individual result reference changed) ───
            // A structural change always implies a result change; otherwise we
            // compare element references to skip spurious notifications.
            var hasResultChange = hasStructuralChange || newResults
                .Select((r, i) => !ReferenceEquals(r, currentResult.ElementAtOrDefault(i)))
                .Any(changed => changed);

            if (!hasStructuralChange && !hasResultChange) return;

            // ── Subscription management (outside _stateLock) ───────────────────
            // Happens before the state write at line ~285. If a newly subscribed
            // inner observer fires OnUpdate synchronously, it will see stale
            // _observers and bail out via the `index < 0` guard in OnUpdate.
            // In the common case this doesn't happen: NotifyManager.Batch defers
            // notifications until the batch callback returns, so OnUpdate runs
            // after the state write below and sees consistent _observers/_result.
            HashSet<QueryObserver<TData, TData>>? newSet = null;
            if (hasStructuralChange)
            {
                var prevSet = new HashSet<QueryObserver<TData, TData>>(
                    prevObservers, ReferenceEqualityComparer.Instance);
                newSet = new HashSet<QueryObserver<TData, TData>>(
                    newObservers, ReferenceEqualityComparer.Instance);

                // TOCTOU: HasListeners can change between the check and the
                // subscription management below (a concurrent last-listener
                // unsubscribe could trigger Destroy). This is benign because
                // SetQueries is expected to be called from UI/main-thread
                // contexts where listener churn doesn't race. The worst case
                // (background caller) is a redundant inner subscribe that gets
                // cleaned up on the next SetQueries or Destroy call.
                if (HasListeners)
                {
                    // Remove subscription handles under _stateLock, then dispose
                    // outside to avoid holding the lock during external callbacks.
                    var removed = new List<IDisposable>();
                    lock (_stateLock)
                    {
                        foreach (var obs in prevObservers.Where(o => !newSet.Contains(o)))
                        {
                            if (_innerSubscriptions.Remove(obs, out var sub))
                                removed.Add(sub);
                        }
                    }

                    foreach (var sub in removed)
                        sub.Dispose();

                    // Subscribe to newly added observers so we start receiving their
                    // updates. Subscribing also triggers ShouldFetchOnMount on each
                    // new observer, which may fire background fetches. Record the
                    // handles under _stateLock.
                    foreach (var obs in newObservers.Where(o => !prevSet.Contains(o)))
                    {
                        var captured = obs;
                        var sub = captured.Subscribe(
                            result => OnUpdate(captured, result));
                        lock (_stateLock) { _innerSubscriptions[captured] = sub; }
                    }
                }
            }

            // ── Atomic state write ──────────────────────────────────────────────
            // _observers, _observerMatches, and _result must be written together
            // so OnUpdate never sees _observers with a different length than _result.
            lock (_stateLock)
            {
                if (hasStructuralChange)
                    _observers = newObservers;
                _observerMatches = newMatches;
                _result = newResults;
            }

            if (HasListeners)
            {
                Notify();
            }
        });
    }

    /// <summary>
    /// Computes an optimistic result snapshot for the given query list by reusing or
    /// creating matching observers, without modifying this instance's observer state
    /// or triggering notifications.
    ///
    /// <para>
    /// Suitable for pre-render or read-only scenarios (e.g. computing the initial
    /// combined state before the first subscription). New observers created here to
    /// satisfy unmatched query keys are not tracked by this instance; they will be
    /// registered with the query cache but not destroyed until the next
    /// <see cref="SetQueries(IReadOnlyList{QueryObserverOptions{TData,TData}})"/> call
    /// that matches their key, or until GC.
    /// </para>
    /// </summary>
    public IReadOnlyList<IQueryResult<TData>> GetOptimisticResult(
        IReadOnlyList<QueryObserverOptions<TData, TData>> queries)
    {
        ArgumentNullException.ThrowIfNull(queries);
        List<QueryObserver<TData, TData>> observers;
        lock (_stateLock) { observers = _observers; }
        var matches = FindMatchingObservers(queries, observers);
        return matches.Select(m => m.Observer.CurrentResult).ToList();
    }

    /// <summary>
    /// Computes an optimistic result snapshot and returns both the raw results, a
    /// delegate to apply <paramref name="combine"/> to those results, and a delegate
    /// to get tracked results. Mirrors TanStack's three-element tuple return from
    /// <c>getOptimisticResult</c>.
    /// </summary>
    /// <typeparam name="TOptCombined">The type returned by <paramref name="combine"/>.</typeparam>
    /// <returns>
    /// A tuple of (rawResults, combineDelegate, getTracked).
    /// The combine delegate accepts an optional result list; when <see langword="null"/> is
    /// passed the raw results computed during this call are used as input.
    /// The getTracked delegate returns results wrapped in <see cref="TrackedQueryResult{TData}"/>.
    /// </returns>
    public (IReadOnlyList<IQueryResult<TData>> RawResults,
            Func<IReadOnlyList<IQueryResult<TData>>?, TOptCombined> GetCombined,
            Func<IReadOnlyList<IQueryResult<TData>>> GetTracked)
        GetOptimisticResult<TOptCombined>(
            IReadOnlyList<QueryObserverOptions<TData, TData>> queries,
            Func<IReadOnlyList<IQueryResult<TData>>, TOptCombined> combine)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(combine);
        List<QueryObserver<TData, TData>> observers;
        lock (_stateLock) { observers = _observers; }
        var matches = FindMatchingObservers(queries, observers);
        var results = matches.Select(m => m.Observer.CurrentResult).ToList();
        // Close over `results` and `matches` so the returned delegates can use them.
        return (
            results,
            r => combine(r ?? results),
            () => TrackResult(results, matches));
    }

    // ── Subscription lifecycle ──────────────────────────────────────────────────

    protected override void OnSubscribe()
    {
        // On the first outer subscription, subscribe to all current inner observers.
        // Each Subscribe() call triggers QueryObserver.OnSubscribe(), which checks
        // ShouldFetchOnMount() and may fire a background fetch for each query.
        if (ListenerCount == 1)
        {
            List<QueryObserver<TData, TData>> observers;
            lock (_stateLock) { observers = _observers; }

            foreach (var observer in observers)
            {
                var captured = observer;
                var sub = captured.Subscribe(
                    result => OnUpdate(captured, result));
                lock (_stateLock) { _innerSubscriptions[captured] = sub; }
            }
        }
    }

    protected override void OnUnsubscribe()
    {
        // When the last outer listener unsubscribes, tear down everything.
        // Matches TanStack's onUnsubscribe which calls destroy().
        if (!HasListeners)
        {
            Destroy();
        }
    }

    // ── Internal update and notification ───────────────────────────────────────

    private void OnUpdate(QueryObserver<TData, TData> observer, IQueryResult<TData> result)
    {
        lock (_stateLock)
        {
            var index = _observers.IndexOf(observer);
            if (index < 0) return;

            // Replace the list at the updated index. We build a fresh list rather than
            // mutating the existing one so that listeners holding a reference to a
            // previous snapshot continue to see a stable, immutable view.
            var newResult = new List<IQueryResult<TData>>(_result);
            newResult[index] = result;
            _result = newResult;
        }

        Notify();
    }

    /// <summary>
    /// Wraps raw results in <see cref="TrackedQueryResult{TData}"/> for observers
    /// that don't have an explicit <see cref="QueryObserverOptions{TData,TQueryData}.NotifyOnChangeProps"/>.
    /// Property accesses on the tracked wrapper are synchronized across all observers
    /// in the group — accessing a property on one tracked result records it on ALL
    /// observers. Mirrors TanStack's <c>#trackResult</c> (<c>queriesObserver.ts:295–309</c>,
    /// PR #7000 synchronization).
    /// </summary>
    private IReadOnlyList<IQueryResult<TData>> TrackResult(
        IReadOnlyList<IQueryResult<TData>> results,
        List<ObserverMatch> matches)
    {
        var tracked = new List<IQueryResult<TData>>(results.Count);

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var match = i < matches.Count ? matches[i] : null;

            // If the observer has explicit NotifyOnChangeProps, return the raw
            // result — the observer already handles its own filtering.
            if (match?.Options.NotifyOnChangeProps is not null)
            {
                tracked.Add(result);
                continue;
            }

            // Wrap in TrackedQueryResult. The onPropTracked callback synchronizes
            // by calling TrackProp on ALL observers in the group, ensuring that
            // accessing `Data` on one observer's tracked result also suppresses
            // Data-unchanged notifications on sibling observers.
            var observer = match?.Observer;
            tracked.Add(observer is not null
                ? observer.TrackResult(result, prop =>
                {
                    // Synchronize across all observers in the group (PR #7000)
                    foreach (var m in matches)
                        m.Observer.TrackProp(prop);
                })
                : result);
        }

        return tracked;
    }

    /// <summary>
    /// Runs the combine function and memoizes the result. Re-runs when:
    /// <list type="bullet">
    /// <item>First call (no previous combined result)</item>
    /// <item>Result list reference changed</item>
    /// <item>Query hashes changed</item>
    /// <item>Combine function reference changed</item>
    /// </list>
    /// Mirrors TanStack's <c>#combineResult</c> (<c>queriesObserver.ts:172–250</c>).
    /// </summary>
    /// <remarks>
    /// TanStack wraps the output in <c>replaceEqualDeep</c> for deep structural
    /// sharing (<c>queriesObserver.ts:241-244</c>). This implementation uses
    /// <see cref="EqualityComparer{T}.Default"/> to detect value-equal results
    /// (works for records) and preserve the previous reference. For deeper sharing,
    /// implement structural sharing inside the combine function itself.
    /// </remarks>
    private TCombinedResult CombineResult(
        IReadOnlyList<IQueryResult<TData>> trackedResults,
        Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? combine,
        List<string>? queryHashes = null)
    {
        if (combine is null)
        {
            // No combine — return the results cast to TCombinedResult. This only
            // works when TCombinedResult is IReadOnlyList<IQueryResult<TData>>,
            // which is the case for the convenience QueriesObserver<TData> subclass.
            return (TCombinedResult)trackedResults;
        }

        // Check if we can reuse the previous combined result. Read memoization
        // state under _stateLock so a concurrent Notify/SetQueries can't tear it.
        lock (_stateLock)
        {
            if (_hasCombinedResult
                && ReferenceEquals(_lastCombineInput, trackedResults)
                && ReferenceEquals(_lastCombine, combine)
                && QueryHashesMatch(queryHashes, _lastQueryHashes))
            {
                return _combinedResult!;
            }
        }

        // Call the user's combine function outside the lock. Under concurrent
        // Notify, both callers may reach here; last writer wins the memo cache.
        var result = combine(trackedResults);

        // TanStack queriesObserver.ts:241-244 — structural sharing on combine output.
        // Use EqualityComparer to detect value-equal results (works for records) and
        // preserve the previous reference. For deeper sharing, implement it inside the
        // combine function itself. Write memoization state under _stateLock.
        lock (_stateLock)
        {
            if (_hasCombinedResult
                && EqualityComparer<TCombinedResult>.Default.Equals(_combinedResult!, result))
            {
                result = _combinedResult!;
            }

            _combinedResult = result;
            _hasCombinedResult = true;
            _lastCombineInput = trackedResults;
            _lastCombine = combine;
            _lastQueryHashes = queryHashes;
        }

        return result;
    }

    private static bool QueryHashesMatch(List<string>? a, List<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private void Notify()
    {
        if (!HasListeners) return;

        // Snapshot the fields that SetQueries can replace concurrently.
        // _stateLock serializes this read against both OnUpdate and SetQueries
        // writes, ensuring _result, _observerMatches, and _combine are from
        // the same logical state.
        IReadOnlyList<IQueryResult<TData>> resultSnapshot;
        List<ObserverMatch> matchesSnapshot;
        Func<IReadOnlyList<IQueryResult<TData>>, TCombinedResult>? combineSnapshot;
        TCombinedResult? previousCombined;
        bool hadPreviousCombined;
        lock (_stateLock)
        {
            resultSnapshot = _result;
            matchesSnapshot = _observerMatches;
            combineSnapshot = _combine;
            previousCombined = _combinedResult;
            hadPreviousCombined = _hasCombinedResult;
        }

        // When a combine function is configured, apply tracking and memoization.
        // Without combine, skip tracking entirely to avoid polluting observer-level
        // _trackedProps (which would suppress legitimate per-observer notifications).
        if (combineSnapshot is not null)
        {
            var trackedResults = TrackResult(resultSnapshot, matchesSnapshot);
            var newCombined = CombineResult(trackedResults, combineSnapshot);

            // Suppress notification if combined result unchanged (reference equality).
            // Only suppress after the first combine has run.
            if (hadPreviousCombined && ReferenceEquals(previousCombined, newCombined))
                return;
        }

        var listenerSnapshot = GetListenerSnapshot();
        _client.NotifyManager.Batch(() =>
        {
            foreach (var listener in listenerSnapshot)
            {
                listener(resultSnapshot);
            }
        });
    }

    // ── Observer matching ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of observer matches for <paramref name="queries"/>, reusing
    /// existing observers where the query hash matches and creating new ones where
    /// no match is found.
    ///
    /// <para>
    /// When multiple queries share the same hash (duplicate keys), existing observers
    /// are consumed in FIFO order to prevent double-reuse. Each reused observer has
    /// its options updated via <see cref="QueryObserver{TData,TQueryData}.SetOptions"/>
    /// so that changes to non-key properties (QueryFn, StaleTime, etc.) take effect
    /// without triggering a key-change refetch.
    /// </para>
    /// </summary>
    private List<ObserverMatch> FindMatchingObservers(
        IReadOnlyList<QueryObserverOptions<TData, TData>> queries,
        List<QueryObserver<TData, TData>> currentObservers)
    {
        // Build a hash → observer queue from the snapshotted observer list. Using
        // a queue per hash handles duplicate-key queries: the first occurrence in
        // the new list reuses the first queued observer; additional occurrences
        // get new observers.
        var available = new Dictionary<string, Queue<QueryObserver<TData, TData>>>();
        foreach (var observer in currentObservers)
        {
            var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(observer.Options.QueryKey);
            if (!available.TryGetValue(hash, out var queue))
            {
                queue = new Queue<QueryObserver<TData, TData>>();
                available[hash] = queue;
            }
            queue.Enqueue(observer);
        }

        var matches = new List<ObserverMatch>(queries.Count);
        foreach (var options in queries)
        {
            var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(options.QueryKey);
            QueryObserver<TData, TData> observer;

            if (available.TryGetValue(hash, out var queue) && queue.Count > 0)
            {
                // Reuse an existing observer, propagating option changes that don't
                // affect the query key (e.g. a new QueryFn or StaleTime).
                observer = queue.Dequeue();
                observer.SetOptions(options);
            }
            else
            {
                observer = new QueryObserver<TData, TData>(_client, options);
            }

            matches.Add(new(options, observer));
        }

        return matches;
    }

    internal sealed record ObserverMatch(
        QueryObserverOptions<TData, TData> Options,
        QueryObserver<TData, TData> Observer);
}

/// <summary>
/// Convenience subclass for the common case where no combine function is used.
/// The combined result type defaults to <see cref="IReadOnlyList{T}"/> of
/// <see cref="IQueryResult{TData}"/>, preserving backward compatibility with
/// existing callers.
/// </summary>
public sealed class QueriesObserver<TData>
    : QueriesObserver<TData, IReadOnlyList<IQueryResult<TData>>>
{
    public QueriesObserver(
        QueryClient client,
        IReadOnlyList<QueryObserverOptions<TData, TData>> queries)
        : base(client, queries) { }
}
