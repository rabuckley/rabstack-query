namespace RabstackQuery;

/// <summary>
/// Callback invoked after a mutation completes (success or error).
/// </summary>
public delegate Task MutationCacheOnSettledCallback(
    object? data, Exception? error, object? variables, object? onMutateResult,
    Mutation mutation, MutationFunctionContext functionContext);
