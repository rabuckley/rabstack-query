namespace RabstackQuery;

/// <summary>
/// Dispatched when a query fetch fails after exhausting retries. Transitions
/// the query to <see cref="QueryStatus.Error"/>.
/// </summary>
internal sealed class ErrorAction : DispatchAction
{
    public required Exception Error { get; init; }
}
