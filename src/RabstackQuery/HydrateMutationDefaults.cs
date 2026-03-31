namespace RabstackQuery;

/// <summary>
/// Default options applied to mutations during hydration.
/// </summary>
public sealed class HydrateMutationDefaults
{
    public TimeSpan? GcTime { get; init; }

    public int? Retry { get; init; }

    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

    /// <inheritdoc cref="MutationOptions{TData, TError, TVariables, TOnMutateResult}.MutationFn"/>
    public Func<object, MutationFunctionContext, CancellationToken, Task<object>>? MutationFn { get; init; }
}
