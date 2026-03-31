namespace RabstackQuery;

/// <summary>
/// Dispatched when a mutation is cancelled via <see cref="System.Threading.CancellationToken"/>.
/// Clears the paused flag. Does not fire cache notifications — cancellation is
/// a C#-specific concern with no TanStack equivalent.
/// </summary>
internal sealed class MutationCancelAction : MutationDispatchAction;
