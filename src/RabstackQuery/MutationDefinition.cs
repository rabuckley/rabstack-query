namespace RabstackQuery;

/// <summary>
/// Reusable mutation definition that bundles the mutation function and common
/// configuration into a single typed object. Analogous to <see cref="QueryOptions{TData}"/>
/// for queries — the return value can be passed to MVVM <c>UseMutation</c> overloads
/// with <typeparamref name="TData"/> and <typeparamref name="TVariables"/> inferred
/// from the definition rather than specified explicitly at the call site.
/// <para>
/// For mutations that need optimistic updates (OnMutate), use
/// <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/> instead.
/// </para>
/// </summary>
public sealed class MutationDefinition<TData, TVariables>
{
    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.MutationFn"/>
    public required Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> MutationFn { get; init; }

    /// <summary>Optional mutation key for identifying this mutation.</summary>
    public QueryKey? MutationKey { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Retry"/>
    public int? Retry { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.RetryDelay"/>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.GcTime"/>
    public TimeSpan GcTime { get; init; } = QueryTimeDefaults.GcTime;

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.NetworkMode"/>
    public NetworkMode NetworkMode { get; init; } = NetworkMode.Online;

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Meta"/>
    public Meta? Meta { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Scope"/>
    public MutationScope? Scope { get; init; }

    /// <summary>
    /// Converts to <see cref="MutationOptions{TData, TVariables}"/>, optionally merging
    /// lifecycle callbacks from <paramref name="callbacks"/>.
    /// </summary>
    internal MutationOptions<TData, Exception, TVariables, object?> ToMutationOptions(
        MutationCallbacks<TData, TVariables>? callbacks = null)
    {
        var options = new MutationOptions<TData, Exception, TVariables, object?>
        {
            MutationFn = MutationFn,
            MutationKey = MutationKey,
            Retry = Retry,
            RetryDelay = RetryDelay,
            GcTime = GcTime,
            NetworkMode = NetworkMode,
            Meta = Meta,
            Scope = Scope,
        };

        if (callbacks is null)
            return options;

        // Adapt 3-param callbacks to the 4-param MutationOptions signature,
        // passing null for the TOnMutateResult? parameter that doesn't exist
        // in the simplified form. Same pattern as MutationCallbacks.ToMutationOptions().
        return options with
        {
            OnSuccess = callbacks.OnSuccess is { } onSuccess
                ? (data, vars, _, ctx) => onSuccess(data, vars, ctx)
                : null,
            OnError = callbacks.OnError is { } onError
                ? (err, vars, _, ctx) => onError(err, vars, ctx)
                : null,
            OnSettled = callbacks.OnSettled is { } onSettled
                ? (data, err, vars, _, ctx) => onSettled(data, err, vars, ctx)
                : null,
        };
    }
}

/// <summary>
/// Reusable mutation definition with optimistic update support. Bundles the mutation
/// function, the <see cref="OnMutate"/> callback, and lifecycle hooks into a single typed
/// object. All three type parameters are inferred from the two required delegates via
/// <see cref="OptimisticMutationDefinition.Create{TData, TVariables, TOnMutateResult}"/>.
/// </summary>
public sealed class OptimisticMutationDefinition<TData, TVariables, TOnMutateResult>
{
    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.MutationFn"/>
    public required Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> MutationFn { get; init; }

    /// <summary>
    /// Called before the mutation function fires. The return value is stored as context
    /// and passed to <see cref="OnError"/> and <see cref="OnSettled"/> for rollback.
    /// </summary>
    public required Func<TVariables, MutationFunctionContext, Task<TOnMutateResult>> OnMutate { get; init; }

    /// <summary>Called after the mutation succeeds.</summary>
    public Func<TData, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSuccess { get; init; }

    /// <summary>Called after the mutation fails.</summary>
    public Func<Exception, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnError { get; init; }

    /// <summary>Called after the mutation completes (success or error).</summary>
    public Func<TData?, Exception?, TVariables, TOnMutateResult?, MutationFunctionContext, Task>? OnSettled { get; init; }

    /// <inheritdoc cref="MutationDefinition{TData, TVariables}.MutationKey"/>
    public QueryKey? MutationKey { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Retry"/>
    public int? Retry { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.RetryDelay"/>
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.GcTime"/>
    public TimeSpan GcTime { get; init; } = QueryTimeDefaults.GcTime;

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.NetworkMode"/>
    public NetworkMode NetworkMode { get; init; } = NetworkMode.Online;

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Meta"/>
    public Meta? Meta { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.Scope"/>
    public MutationScope? Scope { get; init; }

    /// <summary>
    /// Converts to the full <see cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}"/>
    /// with all lifecycle hooks and configuration properties mapped directly.
    /// </summary>
    internal MutationOptions<TData, Exception, TVariables, TOnMutateResult> ToMutationOptions() => new()
    {
        MutationFn = MutationFn,
        OnMutate = OnMutate,
        OnSuccess = OnSuccess,
        OnError = OnError,
        OnSettled = OnSettled,
        MutationKey = MutationKey,
        Retry = Retry,
        RetryDelay = RetryDelay,
        GcTime = GcTime,
        NetworkMode = NetworkMode,
        Meta = Meta,
        Scope = Scope,
    };
}

/// <summary>
/// Static factory for creating <see cref="MutationDefinition{TData, TVariables}"/> with type
/// inference from the mutation function delegate.
/// </summary>
public static class MutationDefinition
{
    /// <summary>
    /// Creates a <see cref="MutationDefinition{TData, TVariables}"/> inferring
    /// <typeparamref name="TData"/> and <typeparamref name="TVariables"/> from the
    /// <paramref name="mutationFn"/> delegate.
    /// </summary>
    public static MutationDefinition<TData, TVariables> Create<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
    {
        return new MutationDefinition<TData, TVariables> { MutationFn = mutationFn };
    }
}

/// <summary>
/// Static factory for creating <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/>
/// with type inference from the mutation function and OnMutate delegates.
/// </summary>
public static class OptimisticMutationDefinition
{
    /// <summary>
    /// Creates an <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/>
    /// inferring all three type parameters from the two delegate signatures.
    /// </summary>
    public static OptimisticMutationDefinition<TData, TVariables, TOnMutateResult>
        Create<TData, TVariables, TOnMutateResult>(
            Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
            Func<TVariables, MutationFunctionContext, Task<TOnMutateResult>> onMutate)
    {
        return new OptimisticMutationDefinition<TData, TVariables, TOnMutateResult>
        {
            MutationFn = mutationFn,
            OnMutate = onMutate,
        };
    }
}
