namespace RabstackQuery;

/// <summary>
/// Provides a sentinel query function that disables a query without using
/// <c>Enabled = false</c>. This is the C# equivalent of TanStack Query's
/// <c>skipToken</c> — a composable alternative for dependent queries where
/// the consumer sets <c>QueryFn = SkipToken.QueryFn&lt;TData&gt;()</c> when
/// data dependencies aren't ready, then switches to a real function via
/// <c>SetOptions</c> when they become available.
/// <para>
/// TanStack reference: <c>utils.ts:423-424, 435-449</c>.
/// </para>
/// </summary>
public static class SkipToken
{
    /// <summary>
    /// Returns the per-<typeparamref name="TData"/> cached sentinel delegate.
    /// Passing this as <c>QueryFn</c> causes the query to be treated as disabled.
    /// </summary>
    public static Func<QueryFunctionContext, Task<TData>> QueryFn<TData>()
        => Sentinel<TData>.Instance;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="queryFn"/> is the
    /// skip-token sentinel (checked by reference equality).
    /// </summary>
    public static bool IsSkipToken<TData>(Func<QueryFunctionContext, Task<TData>>? queryFn)
        => ReferenceEquals(queryFn, Sentinel<TData>.Instance);

    /// <summary>
    /// Generic cache holding one sentinel delegate per <typeparamref name="TData"/>.
    /// The delegate throws <see cref="InvalidOperationException"/> if invoked,
    /// mirroring TanStack's <c>ensureQueryFn</c> safety net.
    /// </summary>
    private static class Sentinel<TData>
    {
        internal static readonly Func<QueryFunctionContext, Task<TData>> Instance = _ =>
            throw new InvalidOperationException(
                "A query with skipToken should never be executed. " +
                "This indicates a bug in RabstackQuery — the query should have been " +
                "disabled before reaching the fetch path.");
    }
}
