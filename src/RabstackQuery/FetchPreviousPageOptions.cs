namespace RabstackQuery;

public sealed record FetchPreviousPageOptions
{
    /// <summary>
    /// Gets or initializes a value that indicates whether a failed fetch throws.
    /// The default is <see langword="false"/>. Errors remain available in the query state.
    /// </summary>
    public bool ThrowOnError { get; init; }

    /// <summary>
    /// Gets or initializes a value that indicates whether to cancel an in-flight fetch and
    /// discard its result. When <see langword="false"/>, the call waits for that fetch instead
    /// of fetching a page. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A fetch with no cached data is never cancelled.
    /// </remarks>
    public bool CancelRefetch { get; init; } = true;
}
