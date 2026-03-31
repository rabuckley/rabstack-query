using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

// ── MutationCache event types ──────────────────────────────────────────
// Mirrors the QueryCacheNotifyEvent hierarchy at QueryClient.cs.
// ActionType is a string rather than a class hierarchy because mutations
// have no reducer/DispatchAction — DevTools only uses it for display.

/// <summary>
/// Cache for managing mutation instances.
/// </summary>
public sealed class MutationCache : Subscribable<MutationCacheListener>
{
    private ILogger _logger;
    private INotifyManager _notifyManager = null!;
    private readonly ConcurrentDictionary<int, Mutation> _mutations = new();
    private int _nextId;

    // Each scope ID maps to a list of all in-flight TaskCompletionSources for that
    // scope. Scoped mutations chain their execution onto the previous one's task
    // to guarantee serial ordering within a scope. We store ALL in-flight TCSs
    // (not just the latest) so that Clear() can complete every gate — including
    // ones that a paused mutation is awaiting, not just the tail of the chain.
    //
    // Divergence from TanStack: TS stores Mutation objects per scope and uses a
    // pause/continue mechanism in the retryer. C# uses task chaining instead,
    // which eliminates the race between "canRun check" and "runNext call" that
    // would require locking in a multi-threaded environment.
    private readonly Dictionary<string, List<TaskCompletionSource>> _scopeQueue = new();
    private readonly Lock _scopeLock = new();

    /// <summary>
    /// Cache-level callbacks that fire for every mutation. Cache-level callbacks
    /// run before the corresponding mutation-level callbacks.
    /// </summary>
    public MutationCacheConfig Config { get; }

    public MutationCache() : this(null, null) { }

    public MutationCache(MutationCacheConfig? config) : this(config, null) { }

    public MutationCache(MutationCacheConfig? config, ILoggerFactory? loggerFactory)
    {
        Config = config ?? MutationCacheConfig.Empty;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MutationCache>();
    }

    /// <summary>
    /// Called by <see cref="QueryClient"/> after construction to wire up the logger.
    /// <see cref="MutationCache"/> may be created before <see cref="QueryClient"/>, so
    /// the logger must be (re)set after the client is constructed — matching the same
    /// post-construction pattern used by <see cref="QueryCache"/>.
    /// </summary>
    internal void SetLoggerFactory(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MutationCache>();
    }

    /// <summary>
    /// Called by <see cref="QueryClient"/> after construction to wire up the
    /// per-client notification manager. Same post-construction pattern as
    /// <see cref="SetLoggerFactory"/>.
    /// </summary>
    internal void SetNotifyManager(INotifyManager notifyManager)
    {
        _notifyManager = notifyManager;
    }

    /// <summary>
    /// Creates or retrieves a mutation with the given options.
    /// </summary>
    public Mutation<TData, TError, TVariables, TOnMutateResult> GetOrCreate<TData, TError, TVariables, TOnMutateResult>(
        QueryClient client,
        MutationOptions<TData, TError, TVariables, TOnMutateResult> options,
        MutationState<TData, TVariables, TOnMutateResult>? state = null)
        where TError : Exception
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        // Apply key-prefix and global defaults before creating the mutation.
        // Mirrors TanStack's mutationCache.build() at mutationCache.ts:114.
        options = client.DefaultMutationOptions(options);

        var mutationId = Interlocked.Increment(ref _nextId);
        var mutation = new Mutation<TData, TError, TVariables, TOnMutateResult>(client, this, mutationId, options, state);

        _mutations[mutationId] = mutation;
        _logger.MutationCacheBuild(mutationId);

        Notify(new MutationCacheAddedEvent { Mutation = mutation });

        return mutation;
    }

    /// <summary>
    /// Removes a mutation from the cache.
    /// </summary>
    public void Remove(Mutation mutation)
    {
        _mutations.TryRemove(mutation.MutationId, out _);
        _logger.MutationCacheRemove(mutation.MutationId);

        // Unconditional, matching TanStack's mutationCache.remove() which always
        // notifies even when the mutation wasn't in the cache.
        Notify(new MutationCacheRemovedEvent { Mutation = mutation });
    }

    /// <summary>
    /// Clears all mutations from the cache. Completes all in-flight scope gates
    /// so that any mutation awaiting its turn is unblocked rather than deadlocked.
    /// </summary>
    public void Clear()
    {
        // Batch-notify removed for each mutation before clearing, matching
        // TanStack's mutationCache.clear() at mutationCache.ts:190-198.
        _notifyManager.Batch(() =>
        {
            foreach (var mutation in _mutations.Values)
                Notify(new MutationCacheRemovedEvent { Mutation = mutation });
            _mutations.Clear();
        });

        lock (_scopeLock)
        {
            foreach (var gates in _scopeQueue.Values)
                foreach (var gate in gates)
                    gate.TrySetResult();
            _scopeQueue.Clear();
        }
    }

    /// <summary>
    /// Registers <paramref name="gate"/> as the latest in-flight gate for
    /// <paramref name="scopeId"/> and returns the previous gate's <see cref="Task"/>
    /// (or <see cref="Task.CompletedTask"/> if this is the first mutation in the scope).
    /// <para>
    /// The caller should <c>await</c> the returned task before starting its work so
    /// that mutations in the same scope execute serially. The caller completes
    /// <paramref name="gate"/> when its work is done, allowing the next queued mutation
    /// to proceed.
    /// </para>
    /// </summary>
    internal Task ExchangeScopeTask(string scopeId, TaskCompletionSource gate)
    {
        lock (_scopeLock)
        {
            if (_scopeQueue.TryGetValue(scopeId, out var gates))
            {
                var previous = gates[^1];
                gates.Add(gate);
                return previous.Task;
            }
            _scopeQueue[scopeId] = [gate];
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Removes <paramref name="expectedGate"/> from the scope queue for
    /// <paramref name="scopeId"/>. If the gate list becomes empty, the scope
    /// key is removed entirely. The identity check ensures a late-finishing
    /// mutation doesn't accidentally remove a gate registered by a newer one.
    /// </summary>
    internal void TryRemoveScopeEntry(string scopeId, TaskCompletionSource expectedGate)
    {
        lock (_scopeLock)
        {
            if (_scopeQueue.TryGetValue(scopeId, out var gates))
            {
                gates.Remove(expectedGate);
                if (gates.Count == 0)
                    _scopeQueue.Remove(scopeId);
            }
        }
    }

    /// <summary>
    /// Wakes all paused mutations by calling <see cref="Mutation.Continue"/>.
    /// Individual errors are swallowed (matching TanStack's <c>.catch(noop)</c>
    /// at mutationCache.ts:231-239) but logged at Warning level.
    /// </summary>
    public void ResumePausedMutations()
    {
        var paused = _mutations.Values.Where(m => m.CurrentIsPaused).ToList();

        if (paused.Count == 0) return;

        _logger.MutationCacheResumingPaused(paused.Count);

        // Batch notifications so observers see a single flush after all mutations
        // are woken, matching TanStack's notifyManager.batch() at mutationCache.ts:234.
        _notifyManager.Batch(() =>
        {
            foreach (var mutation in paused)
            {
                try
                {
                    mutation.Continue();
                }
                catch (Exception ex)
                {
                    _logger.MutationCacheResumeError(mutation.MutationId, ex);
                }
            }
        });
    }

    /// <summary>
    /// Gets all mutations in the cache.
    /// </summary>
    public IEnumerable<Mutation> GetAll() => _mutations.Values;

    /// <summary>
    /// Returns all mutations matching the given filters.
    /// </summary>
    public IEnumerable<Mutation> FindAll(MutationFilters? filters)
    {
        if (filters is null) return GetAll();
        return GetAll().Where(m => MatchMutation(m, filters));
    }

    /// <summary>Returns all mutations.</summary>
    public IEnumerable<Mutation> FindAll() => FindAll(null);

    /// <summary>
    /// Returns the first mutation matching the given filters, or null.
    /// </summary>
    public Mutation? Find(MutationFilters filters)
    {
        return FindAll(filters).FirstOrDefault();
    }

    /// <summary>
    /// Notifies all subscribers of a mutation cache event.
    /// </summary>
    internal void Notify(MutationCacheNotifyEvent @event)
    {
        var snapshot = GetListenerSnapshot();
        _notifyManager.Batch(() =>
        {
            foreach (var listener in snapshot) listener(@event);
        });
    }

    private static bool MatchMutation(Mutation mutation, MutationFilters filters)
    {
        // Key filter
        if (filters.MutationKey is not null)
        {
            if (mutation.MutationKey is null) return false;

            var matches = filters.Exact
                ? QueryKeyMatcher.ExactMatchKey(mutation.MutationKey, filters.MutationKey)
                : QueryKeyMatcher.PartialMatchKey(mutation.MutationKey, filters.MutationKey);

            if (!matches) return false;
        }

        // Status filter
        if (filters.Status is not null && mutation.CurrentStatus != filters.Status.Value)
            return false;

        // Predicate filter
        if (filters.Predicate is not null && !filters.Predicate(mutation))
            return false;

        return true;
    }
}
