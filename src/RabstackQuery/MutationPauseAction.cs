namespace RabstackQuery;

/// <summary>
/// Dispatched when a mutation is paused, either by the retryer (network offline)
/// or by scope-based sequential execution (waiting for a predecessor).
/// Sets <see cref="MutationState{TData,TVariables,TOnMutateResult}.IsPaused"/> to true.
/// </summary>
internal sealed class MutationPauseAction : MutationDispatchAction;
