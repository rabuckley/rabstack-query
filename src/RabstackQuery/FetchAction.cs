namespace RabstackQuery;

/// <summary>
/// Dispatched when a query begins fetching data. Transitions the query to
/// <see cref="FetchStatus.Fetching"/>.
/// </summary>
internal sealed class FetchAction : DispatchAction
{
    public FetchMeta? Meta { get; init; }
}
