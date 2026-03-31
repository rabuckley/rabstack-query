using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Observes mutation state changes and notifies subscribers.
/// </summary>
public sealed class MutationObserver<TData, TError, TVariables, TOnMutateResult>
    : Subscribable<MutationObserverListener<TData, TError>>
    where TError : Exception
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private MutationOptions<TData, TError, TVariables, TOnMutateResult> _options;
    private Mutation<TData, TError, TVariables, TOnMutateResult>? _currentMutation;

    public MutationObserver(
        QueryClient client,
        MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.MutationObserver");
        _options = client.DefaultMutationOptions(options);
    }

    /// <summary>
    /// Updates the observer's options. If the mutation key changed, the observer
    /// resets to idle state (any completed/errored mutation is detached). If the
    /// current mutation is still pending, its options are updated in-place so that
    /// changes like new Meta take effect immediately.
    /// Matches TanStack's <c>mutationObserver.setOptions()</c> in mutationObserver.ts:65-93.
    /// </summary>
    public void SetOptions(MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
    {
        var prevOptions = _options;
        _options = _client.DefaultMutationOptions(options);

        // Notify cache subscribers of options change, matching TanStack's
        // mutationObserver.ts:77-82 which fires observerOptionsUpdated.
        _client.MutationCache.Notify(new MutationCacheObserverOptionsUpdatedEvent
        {
            Mutation = _currentMutation
        });

        // If the mutation key changed, reset the observer to start fresh.
        // Matches TanStack's hashKey comparison in mutationObserver.ts:85-93.
        if (prevOptions.MutationKey is not null
            && _options.MutationKey is not null
            && DefaultQueryKeyHasher.Instance.HashQueryKey(prevOptions.MutationKey)
               != DefaultQueryKeyHasher.Instance.HashQueryKey(_options.MutationKey))
        {
            Reset();
        }
        else if (_currentMutation?.State.Status is MutationStatus.Pending)
        {
            // Forward updated options (e.g., new Meta) to the in-flight mutation.
            _currentMutation.SetOptions(_options);
        }
    }

    /// <summary>
    /// Executes the mutation with the provided variables and optional per-call options.
    /// </summary>
    public async Task<TData> MutateAsync(
        TVariables variables,
        MutateOptions<TData, TError, TVariables, TOnMutateResult>? options = null,
        CancellationToken cancellationToken = default)
    {
        var mutationCache = _client.MutationCache;
        var previousMutation = _currentMutation;
        _currentMutation = mutationCache.GetOrCreate(_client, _options);
        _logger.MutationObserverCreated(_currentMutation.MutationId);

        // Transfer observer registration from the old mutation to the new one.
        // This keeps the GC timer cleared on the active mutation while allowing
        // the previous one to be collected. Matches TanStack's mutationObserver.ts
        // pattern where setOptions() transfers the observer between mutations.
        if (HasListeners)
        {
            previousMutation?.RemoveObserver();
            _currentMutation.AddObserver();
        }

        try
        {
            var result = await _currentMutation.Execute(variables, options, cancellationToken, NotifyListeners);
            NotifyListeners();
            return result;
        }
        catch
        {
            // Notify listeners so the UI reflects the error state, then let the
            // exception propagate to the caller. Without rethrowing, callers have
            // no way to distinguish success from failure.
            NotifyListeners();
            throw;
        }
    }

    /// <summary>
    /// The current mutation result.
    /// </summary>
    public IMutationResult<TData, TError> CurrentResult
    {
        get
        {
            if (_currentMutation is null)
            {
                return new MutationResult<TData, TError, TVariables, TOnMutateResult>(
                    new MutationState<TData, TVariables, TOnMutateResult>
                    {
                        Status = MutationStatus.Idle
                    });
            }

            return new MutationResult<TData, TError, TVariables, TOnMutateResult>(_currentMutation.State);
        }
    }

    /// <summary>
    /// Resets the observer to idle by detaching from the current mutation.
    /// The mutation's own state in the cache is not altered — only the observer's
    /// reference is cleared. Matches TanStack's <c>mutationObserver.reset()</c> which
    /// calls <c>removeObserver(this)</c> and sets <c>#currentMutation = undefined</c>.
    /// </summary>
    public void Reset()
    {
        // Remove observer from the current mutation so its GC timer can fire.
        if (HasListeners)
        {
            _currentMutation?.RemoveObserver();
        }
        _currentMutation = null;
        NotifyListeners();
    }

    protected override void OnSubscribe()
    {
        base.OnSubscribe();

        // Register with the current mutation on first subscriber so the mutation's
        // GC timer is cleared while the observer is active.
        if (ListenerCount == 1)
        {
            _currentMutation?.AddObserver();
        }
    }

    protected override void OnUnsubscribe()
    {
        base.OnUnsubscribe();

        // Unregister from the current mutation when the last subscriber leaves,
        // allowing the mutation to be garbage collected.
        if (ListenerCount == 0)
        {
            _currentMutation?.RemoveObserver();
        }
    }

    private void NotifyListeners()
    {
        var result = CurrentResult;

        var snapshot = GetListenerSnapshot();
        _client.NotifyManager.Batch(() =>
        {
            foreach (var listener in snapshot)
            {
                listener(result);
            }
        });
    }
}

/// <summary>
/// Static factory for creating <see cref="MutationObserver{TData, TError, TVariables, TOnMutateResult}"/>
/// instances with reduced generic boilerplate. Accepts the 2-param
/// <see cref="MutationOptions{TData, TVariables}"/> alias.
/// </summary>
public static class MutationObserver
{
    /// <summary>
    /// Creates a <see cref="MutationObserver{TData, TError, TVariables, TOnMutateResult}"/>
    /// with TError defaulted to <see cref="Exception"/> and TOnMutateResult to <c>object?</c>.
    /// </summary>
    public static MutationObserver<TData, Exception, TVariables, object?> Create<TData, TVariables>(
        QueryClient client,
        MutationOptions<TData, TVariables> options)
    {
        return new MutationObserver<TData, Exception, TVariables, object?>(client, options);
    }
}
