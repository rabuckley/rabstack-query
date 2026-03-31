namespace RabstackQuery;

/// <summary>
/// Base class for mutation state transition actions, consumed by the reducer
/// in <see cref="Mutation{TData,TError,TVariables,TOnMutateResult}"/>. Separate
/// hierarchy from <see cref="DispatchAction"/> (query actions) because the two
/// are never mixed — each is consumed by a different reducer on a different type.
/// </summary>
internal abstract class MutationDispatchAction;
