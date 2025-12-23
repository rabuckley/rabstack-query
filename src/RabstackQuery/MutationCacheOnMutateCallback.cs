namespace RabstackQuery;

/// <summary>
/// Callback invoked before a mutation executes.
/// </summary>
public delegate Task MutationCacheOnMutateCallback(
    object? variables, Mutation mutation, MutationFunctionContext functionContext);
