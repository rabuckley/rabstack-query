namespace RabstackQuery;

/// <summary>
/// Metadata attached to an in-flight fetch, carrying pagination direction
/// and internal state needed for devtools trigger-loading restoration.
/// </summary>
public sealed class FetchMeta
{
    public FetchMore? FetchMore { get; init; }

    /// <summary>
    /// Stores the previous query function as a type-erased delegate during
    /// devtools trigger-loading so it can be restored later.
    /// Mirrors TanStack's <c>fetchMeta.__previousQueryOptions</c>.
    /// </summary>
    internal Delegate? PreviousQueryFn { get; init; }

    /// <summary>
    /// Stores the previous query state as a boxed <c>QueryState&lt;TData&gt;</c>
    /// during devtools trigger-loading/error for restoration.
    /// </summary>
    internal object? PreviousState { get; init; }
}
