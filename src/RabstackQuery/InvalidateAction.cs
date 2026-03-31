namespace RabstackQuery;

/// <summary>
/// Dispatched when a query is marked as invalidated (stale), triggering a
/// refetch if the query has active observers.
/// </summary>
internal sealed class InvalidateAction : DispatchAction
{
}
