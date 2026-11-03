namespace RabstackQuery;

/// <summary>
/// Options for <see cref="QueryObserver{TData,TQueryData}.RefetchAsync"/>.
/// Mirrors TanStack's <c>RefetchOptions</c> interface.
/// </summary>
public sealed record RefetchOptions
{
    /// <summary>
    /// When true, errors from the fetch are thrown to the caller.
    /// When false (default), errors are suppressed — they are already captured
    /// in the query state via <c>ErrorAction</c> dispatch. This matches
    /// TanStack's default <c>throwOnError: false</c> behavior.
    /// </summary>
    public bool ThrowOnError { get; init; }

    /// <summary>
    /// Gets or initializes a value that indicates whether to cancel an in-flight fetch
    /// with existing data and start another. The default is <see langword="true"/>.
    /// If <see langword="false"/>, the existing fetch is reused.
    /// </summary>
    public bool CancelRefetch { get; init; } = true;
}
