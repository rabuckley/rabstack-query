namespace RabstackQuery;

/// <summary>
/// Dispatched when the retryer resumes from a paused state.
/// Sets <see cref="FetchStatus.Fetching"/>. Mirrors TanStack's
/// <c>{ type: 'continue' }</c> action from query.ts:623–627.
/// </summary>
internal sealed class ContinueAction : DispatchAction
{
}
