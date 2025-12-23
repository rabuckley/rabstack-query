namespace RabstackQuery;

/// <summary>
/// Dispatched when the retryer enters a paused state (offline/unfocused).
/// Sets <see cref="FetchStatus.Paused"/>. Mirrors TanStack's
/// <c>{ type: 'pause' }</c> action from query.ts:620–622.
/// </summary>
public sealed class PauseAction : DispatchAction
{
}
