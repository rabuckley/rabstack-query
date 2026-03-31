namespace RabstackQuery;

/// <summary>
/// Dispatched when a paused mutation resumes, either from network recovery
/// or scope predecessor completion.
/// Sets <see cref="MutationState{TData,TVariables,TOnMutateResult}.IsPaused"/> to false.
/// </summary>
internal sealed class MutationContinueAction : MutationDispatchAction;
