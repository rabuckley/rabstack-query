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
    /// When true (default), an in-flight fetch with existing data is cancelled
    /// and a new fetch starts. When false, the existing in-flight fetch is
    /// deduplicated (returned as-is). Mirrors TanStack's
    /// <c>cancelRefetch ?? true</c> at <c>queryClient.ts:321</c>.
    /// </summary>
    public bool CancelRefetch { get; init; } = true;
}
