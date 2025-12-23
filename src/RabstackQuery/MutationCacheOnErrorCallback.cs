namespace RabstackQuery;

/// <summary>
/// Callback invoked when a mutation errors.
/// </summary>
public delegate Task MutationCacheOnErrorCallback(
    Exception error, object? variables, object? onMutateResult,
    Mutation mutation, MutationFunctionContext functionContext);
