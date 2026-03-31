using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Non-generic base class for mutations, analogous to the abstract <see cref="Query"/> base class.
/// Allows the <see cref="MutationCache"/> to store mutations without knowing their type parameters.
/// </summary>
/// <remarks>
/// <para>The only concrete subclass is the internal <c>Mutation&lt;TData, TError, TVariables, TOnMutateResult&gt;</c>.
/// This class is not designed for subclassing outside of RabstackQuery.</para>
/// <para><b>Threading:</b> mutation state mutations and observer notifications are not
/// inherently thread-safe. Callers must ensure mutations are accessed from a single
/// context (typically the UI/synchronization context).</para>
/// </remarks>
public abstract class Mutation : Removable
{
    internal Mutation(TimeProvider timeProvider) : base(timeProvider) { }

    private int _observerCount;

    public int MutationId { get; protected init; }
    public abstract MutationStatus CurrentStatus { get; }
    public abstract bool CurrentIsPaused { get; }
    public abstract QueryKey? MutationKey { get; }
    public abstract Meta? Meta { get; }
    public abstract MutationScope? Scope { get; }
    public abstract void Reset();

    /// <summary>
    /// The cache this mutation belongs to. Set by the generic constructor.
    /// Used to fire <see cref="MutationCacheNotifyEvent"/>s from observer
    /// registration and state changes.
    /// </summary>
    internal MutationCache? Cache { get; set; }

    /// <summary>
    /// Produces a type-erased snapshot of this mutation's state for serialization
    /// and DevTools display. Mirrors <see cref="Query.Dehydrate"/>.
    /// </summary>
    internal abstract DehydratedMutation Dehydrate();

    /// <summary>
    /// Wakes a paused retryer by invoking <see cref="Retryer{TData}.Continue"/>.
    /// If the retryer has already resolved (completed or was never created), this
    /// is a no-op. Does NOT re-execute the mutation — that would risk duplicate
    /// side effects. Mirrors TanStack's <c>mutation.continue()</c>.
    /// </summary>
    public abstract void Continue();

    /// <summary>
    /// Registers an observer with this mutation. Clears the GC timer while
    /// observers are active, matching TanStack's <c>mutation.addObserver()</c>.
    /// </summary>
    internal void AddObserver()
    {
        Interlocked.Increment(ref _observerCount);
        ClearGcTimeout();
        Cache?.Notify(new MutationCacheObserverAddedEvent { Mutation = this });
    }

    /// <summary>
    /// Unregisters an observer from this mutation. Re-schedules GC when the
    /// last observer leaves, matching TanStack's <c>mutation.removeObserver()</c>.
    /// </summary>
    internal void RemoveObserver()
    {
        Interlocked.Decrement(ref _observerCount);
        ScheduleGc();
        Cache?.Notify(new MutationCacheObserverRemovedEvent { Mutation = this });
    }

    /// <summary>Whether any observers are currently subscribed to this mutation.</summary>
    public bool HasObservers => Volatile.Read(ref _observerCount) > 0;
}

/// <summary>
/// Represents a mutation operation with lifecycle management.
/// </summary>
public sealed class Mutation<TData, TError, TVariables, TOnMutateResult> : Mutation
    where TError : Exception
{
    // Divergence from TanStack: TS passes canRun: () => mutationCache.canRun(this) for
    // scope coordination. C# uses task-chaining instead (see scope gate below), which
    // eliminates the race between canRun check and runNext in a multi-threaded environment.
    private static readonly Func<bool> AlwaysCanRun = static () => true;

    private readonly MutationCache _cache;
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private MutationOptions<TData, TError, TVariables, TOnMutateResult> _options;

    // Completed by the finally block of Execute() to signal the next scoped mutation
    // in the queue that it may proceed. Null for unscoped mutations.
    // Volatile: written by Execute's finally block, read by Reset(). Follows the
    // same cross-thread visibility pattern as Retryer._continueFn.
    private volatile TaskCompletionSource? _scopeCompletionGate;

    // The active retryer for the current execution. Null when no execution is
    // in flight. Used by Continue() to wake a network-paused mutation.
    // Volatile: read from event threads (Continue, Reset) and written from
    // async continuations (Execute finally). Without volatile, writes may not
    // be visible cross-thread. Follows the same pattern as Retryer._isRetryCancelled.
    private volatile Retryer<TData>? _retryer;

    public override MutationStatus CurrentStatus => State.Status;

    /// <summary>
    /// Whether this mutation is currently paused. <see cref="MutationState{TData,TVariables,TOnMutateResult}.IsPaused"/>
    /// is set by both scope-pause (waiting for a predecessor in the same
    /// <see cref="MutationScope"/>) and network-pause (retryer paused because
    /// <see cref="NetworkMode.Online"/> and the device is offline).
    /// <para>
    /// <see cref="MutationCache.ResumePausedMutations"/> calls <see cref="Continue"/>
    /// on every paused mutation, including scope-paused ones. This is safe because
    /// scope-paused mutations have not entered <see cref="Retryer{TData}.PauseAsync"/>
    /// yet — their retryer's <c>_continueFn</c> is null, so <see cref="Continue"/>
    /// is a no-op for them.
    /// </para>
    /// </summary>
    public override bool CurrentIsPaused => State.IsPaused;
    public override QueryKey? MutationKey => _options.MutationKey;
    public override Meta? Meta => _options.Meta;
    public override MutationScope? Scope => _options.Scope;

    public MutationState<TData, TVariables, TOnMutateResult> State { get; private set; }

    // ── Dispatch/Reducer ──────────────────────────────────────────────
    // Mirrors Query<TData>.Dispatch: every state transition goes through
    // the reducer, producing a new immutable MutationState and then
    // notifying observers + cache.

    private void Dispatch(MutationDispatchAction action, Action? onStatusChanged)
    {
        State = Reducer(State, action);

        // Map action to the ActionType string expected by MutationCacheUpdatedEvent.
        // Actions that don't fire cache notifications (SetContext, Cancel) map to null.
        var actionType = action switch
        {
            MutationPendingAction<TVariables> => "pending",
            MutationPauseAction => "pause",
            MutationContinueAction => "continue",
            MutationFailAction => "failed",
            MutationSuccessAction<TData> => "success",
            MutationErrorAction => "error",
            MutationSetContextAction<TOnMutateResult> => (string?)null,
            MutationCancelAction => (string?)null,
            _ => throw new InvalidOperationException($"Unknown mutation action: {action.GetType().Name}"),
        };

        if (actionType is not null)
        {
            onStatusChanged?.Invoke();
            _cache.Notify(new MutationCacheUpdatedEvent { Mutation = this, ActionType = actionType });
        }
    }

    private static MutationState<TData, TVariables, TOnMutateResult> Reducer(
        MutationState<TData, TVariables, TOnMutateResult> state,
        MutationDispatchAction action)
    {
        return action switch
        {
            // Pending: clear stale fields from prior execution, set new variables.
            // Context is also cleared (bug fix: previously leaked from prior execution).
            // Mirrors TanStack's pending reducer (mutation.ts:352-364).
            MutationPendingAction<TVariables> pending => new MutationState<TData, TVariables, TOnMutateResult>
            {
                Status = MutationStatus.Pending,
                IsPaused = pending.IsPaused,
                Variables = pending.Variables,
                SubmittedAt = pending.SubmittedAt,
            },

            MutationPauseAction => state with { IsPaused = true },
            MutationContinueAction => state with { IsPaused = false },

            MutationFailAction fail => state with
            {
                FailureCount = fail.FailureCount,
                FailureReason = fail.Error,
            },

            MutationSetContextAction<TOnMutateResult> ctx => state with { Context = ctx.Context },

            MutationSuccessAction<TData> success => state with
            {
                Data = success.Data,
                Error = null,
                FailureCount = 0,
                FailureReason = null,
                Status = MutationStatus.Success,
                IsPaused = false,
            },

            MutationErrorAction error => state with
            {
                Error = error.Error,
                FailureCount = state.FailureCount + 1,
                FailureReason = error.Error,
                Status = MutationStatus.Error,
                IsPaused = false,
            },

            MutationCancelAction => state with { IsPaused = false },

            _ => throw new InvalidOperationException($"Unknown mutation action: {action.GetType().Name}"),
        };
    }

    public Mutation(
        QueryClient client,
        MutationCache cache,
        int mutationId,
        MutationOptions<TData, TError, TVariables, TOnMutateResult> options,
        MutationState<TData, TVariables, TOnMutateResult>? state = null)
        : base(client.TimeProvider)
    {
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mutation");
        _cache = cache;
        Cache = cache; // Base class property for cache event notifications
        MutationId = mutationId; // Assigned via protected init on base class
        _options = options;

        State = state ?? GetDefaultState();

        UpdateGcTime(_options.GcTime);
        ScheduleGc();
    }

    private static MutationState<TData, TVariables, TOnMutateResult> GetDefaultState()
    {
        return new MutationState<TData, TVariables, TOnMutateResult>
        {
            Data = default,
            Error = null,
            FailureCount = 0,
            FailureReason = null,
            IsPaused = false,
            Status = MutationStatus.Idle,
            Variables = default,
            SubmittedAt = 0,
            Context = default
        };
    }

    /// <summary>
    /// Updates this mutation's options. Called by
    /// <see cref="MutationObserver{TData,TError,TVariables,TOnMutateResult}.SetOptions"/>
    /// to forward option changes (e.g., new Meta) to a pending mutation.
    /// Matches TanStack's <c>mutation.setOptions()</c> in mutation.ts:116-121.
    /// </summary>
    public void SetOptions(MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
    {
        _options = options;
        UpdateGcTime(_options.GcTime);
    }

    /// <summary>
    /// Executes the mutation with the provided variables and optional per-call options.
    /// </summary>
    public async Task<TData> Execute(
        TVariables variables,
        MutateOptions<TData, TError, TVariables, TOnMutateResult>? mutateOptions = null,
        CancellationToken cancellationToken = default,
        Action? onStatusChanged = null)
    {
        if (_options.MutationFn is null)
        {
            throw new InvalidOperationException("Mutation function is not set");
        }

        _logger.MutationExecuteStarted(MutationId);

        // Create function context for this mutation. Defined before the retryer so
        // the same instance is captured by both the retryer's Fn lambda and the
        // onMutate/onSuccess/onError/onSettled callbacks.
        var functionContext = new MutationFunctionContext(_client, _options.Meta, _options.MutationKey);

        // Create the retryer up-front so we can check CanStart() before the
        // pending dispatch. This lets us set IsPaused atomically with Status.
        // MaxRetries = Retry + 1 because the retryer counts "total attempts"
        // (first try + retries), while options.Retry counts "retry attempts only".
        // When Retry == 0, MaxRetries == 1 gives a single attempt with no retries,
        // functionally identical to the previous direct-call path.
        var retryerOptions = new RetryerOptions<TData>
        {
            Fn = ct => _options.MutationFn(variables, functionContext, ct),
            MaxRetries = (_options.Retry ?? 0) + 1,
            RetryDelay = _options.RetryDelay,
            TimeProvider = _client.TimeProvider,
            LoggerFactory = _client.LoggerFactory,
            Metrics = _client.Metrics,
            RetrySource = "mutation",
            NetworkMode = _options.NetworkMode,
            OnlineManager = _client.OnlineManager,
            FocusManager = _client.FocusManager,
            CanRun = AlwaysCanRun,
            OnPause = () =>
            {
                _logger.MutationNetworkPaused(MutationId);
                Dispatch(new MutationPauseAction(), onStatusChanged);
            },
            OnContinue = () =>
            {
                _logger.MutationNetworkResumed(MutationId);
                Dispatch(new MutationContinueAction(), onStatusChanged);
            },
            OnFail = (count, error) =>
            {
                Dispatch(new MutationFailAction { FailureCount = count, Error = error }, onStatusChanged);
            }
        };

        var retryer = new Retryer<TData>(retryerOptions);
        _retryer = retryer;

        // Mirrors TanStack's dispatch({ type: 'pending' }) which atomically
        // sets isPaused from canStart() and clears stale fields from any prior
        // execution (mutation.ts:352-364).
        Dispatch(new MutationPendingAction<TVariables>
        {
            Variables = variables,
            SubmittedAt = GetUtcNowMs(),
            IsPaused = !retryer.CanStart(),
        }, onStatusChanged);

        // ── Scope-based sequential execution ──────────────────────────
        // If this mutation has a scope, it must wait for any previously-submitted
        // mutation in the same scope to finish before it can start. This mirrors
        // TanStack's canRun() / runNext() pattern but uses task chaining instead
        // of the pause/continue retryer mechanism. Task chaining is race-free in
        // C#: the previous task is captured atomically under a lock, so there is
        // no window where RunNext could fire before IsPaused is set (as would be
        // possible with the TS approach in a multi-threaded environment).
        //
        // The scope gate exchange, metrics setup, and mutation lifecycle are all
        // wrapped in an outer try/finally that guarantees the scope gate is always
        // completed — even if metrics setup or the await of the previous task
        // throws. Without this, any exception between gate registration and the
        // inner try/finally would permanently deadlock the scope queue.
        TaskCompletionSource? scopeGate = null;
        try
        {
            if (_options.Scope is not null)
            {
                // Register our completion gate first, before any await, so the next
                // mutation in the scope will chain onto this task.
                scopeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _scopeCompletionGate = scopeGate;
                var prevTask = _cache.ExchangeScopeTask(_options.Scope.Id, scopeGate);

                if (!prevTask.IsCompleted)
                {
                    Dispatch(new MutationPauseAction(), onStatusChanged);

                    // Await without capturing the current SynchronizationContext to
                    // avoid deadlocks on UI-thread dispatchers.
                    await prevTask.ConfigureAwait(false);

                    Dispatch(new MutationContinueAction(), onStatusChanged);
                }
            }

            // ── Metrics: record mutation start and begin timing ────────────
            // Tag with the mutation key hash when a MutationKey is set; omit
            // the tag for keyless mutations to avoid unbounded cardinality.
            var metrics = _client.Metrics;
            var mutationKeyHash = _options.MutationKey is not null
                ? DefaultQueryKeyHasher.Instance.HashQueryKey(_options.MutationKey)
                : null;
            var mutationKeyTag = mutationKeyHash is not null
                ? QueryMetrics.MutationKeyTag(mutationKeyHash)
                : default;

            var sw = metrics.MutationDuration is not null ? Stopwatch.StartNew() : null;

            if (mutationKeyHash is not null)
            {
                metrics.MutationTotal?.Add(1, mutationKeyTag);
            }
            else
            {
                metrics.MutationTotal?.Add(1);
            }

            var cacheConfig = _cache.Config;

            try
            {
                // Cache-level onMutate runs before mutation-level onMutate.
                // This matches TanStack's ordering where mutationCache.config.onMutate
                // is awaited before options.onMutate.
                if (cacheConfig.OnMutate is not null)
                {
                    await cacheConfig.OnMutate(variables, this, functionContext);
                }

                // Mutation-level onMutate captures context for subsequent callbacks
                if (_options.OnMutate is not null)
                {
                    _logger.MutationOnMutateInvoked(MutationId);
                    var context = await _options.OnMutate(variables, functionContext);
                    Dispatch(new MutationSetContextAction<TOnMutateResult> { Context = context }, onStatusChanged);
                }

                // Always route through the retryer — even when Retry == 0 (MaxRetries == 1).
                // This gives us NetworkMode enforcement for free: the retryer pauses
                // before executing when offline and resumes on connectivity change.
                TData data;
                using var registration = cancellationToken.Register(() => retryer.Cancel());
                data = await retryer.ExecuteAsync();

                // Success-path callbacks are intentionally NOT individually try-catch
                // wrapped, matching TanStack's semantics. If any callback throws, the
                // exception falls through to the catch block and the mutation is treated
                // as failed — including cache-level OnError firing for the callback's
                // exception. This means a buggy global OnSuccess handler can
                // retroactively mark a successful mutation as errored.
                //
                // State is updated AFTER all callbacks succeed, matching TanStack's
                // dispatch ordering where `dispatch({ type: 'success' })` happens
                // after callbacks complete.

                // Cache-level onSuccess runs before mutation-level onSuccess
                if (cacheConfig.OnSuccess is not null)
                {
                    await cacheConfig.OnSuccess(data, variables, State.Context, this, functionContext);
                }

                var onSuccess = mutateOptions?.OnSuccess ?? _options.OnSuccess;
                if (onSuccess is not null)
                {
                    await onSuccess(data, variables, State.Context, functionContext);
                }

                // Cache-level onSettled runs before mutation-level onSettled
                if (cacheConfig.OnSettled is not null)
                {
                    await cacheConfig.OnSettled(data, null, variables, State.Context, this, functionContext);
                }

                var onSettled = mutateOptions?.OnSettled ?? _options.OnSettled;
                if (onSettled is not null)
                {
                    await onSettled(data, null, variables, State.Context, functionContext);
                }

                // Set success state AFTER callbacks complete without error
                Dispatch(new MutationSuccessAction<TData> { Data = data }, onStatusChanged);

                sw?.Stop();
                if (mutationKeyHash is not null)
                {
                    metrics.MutationSuccessTotal?.Add(1, mutationKeyTag);
                    if (sw is not null)
                    {
                        metrics.MutationDuration?.Record(sw.Elapsed.TotalSeconds, mutationKeyTag);
                    }
                }
                else
                {
                    metrics.MutationSuccessTotal?.Add(1);
                    if (sw is not null)
                    {
                        metrics.MutationDuration?.Record(sw.Elapsed.TotalSeconds);
                    }
                }

                _logger.MutationExecuteSucceeded(MutationId);
                return data;
            }
            catch (OperationCanceledException)
            {
                // C#-specific: cancellation is not a mutation error. TanStack doesn't
                // distinguish these, but C#'s CancellationToken model makes this a
                // natural concern — Query.cs already has the same pattern (line 612-619).
                sw?.Stop();
                Dispatch(new MutationCancelAction(), onStatusChanged);
                if (mutationKeyHash is not null)
                {
                    metrics.MutationCancelledTotal?.Add(1, mutationKeyTag);
                }
                else
                {
                    metrics.MutationCancelledTotal?.Add(1);
                }

                _logger.MutationCancelled(MutationId);
                throw;
            }
            catch (Exception ex)
            {
                sw?.Stop();
                if (mutationKeyHash is not null)
                {
                    metrics.MutationErrorTotal?.Add(1, mutationKeyTag);
                    if (sw is not null)
                    {
                        metrics.MutationDuration?.Record(sw.Elapsed.TotalSeconds, mutationKeyTag);
                    }
                }
                else
                {
                    metrics.MutationErrorTotal?.Add(1);
                    if (sw is not null)
                    {
                        metrics.MutationDuration?.Record(sw.Elapsed.TotalSeconds);
                    }
                }

                // State.Error/FailureReason are typed Exception? so they always capture
                // the real exception regardless of TError. The downcast to TError is only
                // used for the mutation-level OnError callback (which requires TError);
                // OnSettled always runs since it accepts nullable TError.
                var error = ex as TError;

                // Logging and metrics are recorded at the point of failure, before
                // callbacks run, since they capture the failure event itself rather
                // than the state transition that follows.
                _logger.MutationFailed(MutationId, ex);

                // Each error-path callback is wrapped individually so that one callback
                // throwing doesn't prevent the next from running. TanStack forwards
                // these errors via `void Promise.reject(e)` (unhandled rejection); in
                // C# we swallow them since the original mutation error propagates.

                // Cache-level onError runs before mutation-level onError
                if (cacheConfig.OnError is not null)
                {
                    try
                    {
                        await cacheConfig.OnError(ex, variables, State.Context, this, functionContext);
                    }
                    catch (Exception callbackEx)
                    {
                        _logger.MutationOnErrorCallbackThrew(MutationId, callbackEx);
                    }
                }

                if (error is not null)
                {
                    var onError = mutateOptions?.OnError ?? _options.OnError;
                    if (onError is not null)
                    {
                        try
                        {
                            await onError(error, variables, State.Context, functionContext);
                        }
                        catch (Exception callbackEx)
                        {
                            _logger.MutationOnErrorCallbackThrew(MutationId, callbackEx);
                        }
                    }
                }

                // Cache-level onSettled runs before mutation-level onSettled
                if (cacheConfig.OnSettled is not null)
                {
                    try
                    {
                        await cacheConfig.OnSettled(default, ex, variables, State.Context, this, functionContext);
                    }
                    catch (Exception callbackEx)
                    {
                        _logger.MutationOnSettledCallbackThrew(MutationId, callbackEx);
                    }
                }

                var onSettled = mutateOptions?.OnSettled ?? _options.OnSettled;
                if (onSettled is not null)
                {
                    try
                    {
                        await onSettled(default, error, variables, State.Context, functionContext);
                    }
                    catch (Exception callbackEx)
                    {
                        _logger.MutationOnSettledCallbackThrew(MutationId, callbackEx);
                    }
                }

                // Divergence from TanStack: C# defers the state update to after all
                // callbacks complete, matching the TanStack dispatch ordering where
                // `dispatch({ type: 'error' })` fires only after all callbacks finish.
                // Before this fix, state was set before callbacks ran, so cache-level
                // OnError would see Status == Error instead of Pending.
                Dispatch(new MutationErrorAction { Error = ex }, onStatusChanged);

                throw;
            }
        } // outer try
        finally
        {
            // Signal the next queued mutation in this scope that it may now start.
            // Matches TanStack's `mutationCache.runNext(this)` in the finally block.
            //
            // This is in the OUTER finally so it runs even if metrics setup or the
            // scope queue await throws — preventing permanent deadlock of the scope.
            scopeGate?.TrySetResult();
            _scopeCompletionGate = null;

            // Clean up the scope queue entry if this mutation's gate is still the
            // latest for its scope. If a newer mutation already replaced it, the
            // identity check in TryRemoveScopeEntry is a no-op.
            if (scopeGate is not null && _options.Scope is not null)
            {
                _cache.TryRemoveScopeEntry(_options.Scope.Id, scopeGate);
            }

            // Dispose the retryer so its CTS is cleaned up, and null the field
            // so Continue() becomes a no-op after the mutation completes.
            retryer.Dispose();
            _retryer = null;
        }
    }

    public override void Continue()
    {
        // Capture to a local to prevent a torn read — another thread may null
        // _retryer between the null check and the dereference.
        var retryer = _retryer;
        if (retryer is not null)
        {
            retryer.Continue();
        }
        else if (State.IsPaused && State.Variables is not null)
        {
            // Re-execute path for hydrated paused mutations.
            // Divergence from TanStack: TS returns this.#currentMutation ?? this.execute(variables).
            // C# Continue() is void, so we fire-and-forget.
            // TanStack source: mutation.ts — continue() method.
            //
            // Exceptions from Execute are handled internally (dispatched as error
            // state and surfaced via MutationObserver notifications), so the
            // discarded task won't produce unobserved exceptions.
            var variables = State.Variables;
            Debug.Assert(variables is not null); // guarded by 'is not null' check above
            _ = Execute(variables);
        }
    }

    /// <summary>
    /// Resets the mutation to its initial state.
    /// </summary>
    public override void Reset()
    {
        var scopeGate = _scopeCompletionGate;
        _scopeCompletionGate = null;
        scopeGate?.TrySetResult();

        var retryer = _retryer;
        _retryer = null;
        retryer?.Dispose();

        State = GetDefaultState();
    }

    internal override DehydratedMutation Dehydrate()
    {
        return new DehydratedMutation
        {
            MutationKey = MutationKey,
            State = new DehydratedMutationState
            {
                Data = State.Data,
                Error = State.Error,
                FailureCount = State.FailureCount,
                FailureReason = State.FailureReason,
                IsPaused = State.IsPaused,
                Status = State.Status,
                Variables = State.Variables,
                SubmittedAt = State.SubmittedAt,
                Context = State.Context,
            },
            Meta = Meta,
            Scope = Scope,
        };
    }

    protected override void OptionalRemove()
    {
        if (!HasObservers)
        {
            // A pending (in-flight) mutation must never be removed — reschedule
            // and check again once the GC timer fires after completion.
            // Matches TanStack's optionalRemove() which calls scheduleGc() when
            // status === 'pending' rather than removing immediately.
            if (CurrentStatus == MutationStatus.Pending)
            {
                ScheduleGc();
            }
            else
            {
                _logger.MutationGcRemoved(MutationId);
                _cache.Remove(this);
            }
        }
    }
}
