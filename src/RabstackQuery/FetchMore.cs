namespace RabstackQuery;

/// <summary>
/// Specifies the pagination direction for an infinite query fetch.
/// </summary>
public sealed class FetchMore
{
    public required FetchDirection Direction { get; init; }
}
