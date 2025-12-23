namespace RabstackQuery;

/// <summary>
/// Callback invoked when a mutation succeeds.
/// </summary>
public delegate Task MutationCacheOnSuccessCallback(
    object? data, object? variables, object? onMutateResult,
    Mutation mutation, MutationFunctionContext functionContext);
