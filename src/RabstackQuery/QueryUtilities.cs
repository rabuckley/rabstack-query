namespace RabstackQuery;

/// <summary>
/// Helpers for constructing <see cref="QueryObserverOptions{TData,TQueryData}.PlaceholderData"/>
/// delegates.
/// </summary>
public static class QueryUtilities
{
    /// <summary>
    /// Placeholder data function that returns the previous query's data unchanged.
    /// Assign as a method group to <c>PlaceholderData</c> for the "keep previous data"
    /// pattern (pagination, query key switches):
    /// <code>PlaceholderData = QueryUtilities.KeepPreviousData&lt;MyType&gt;</code>
    /// </summary>
    public static TQueryData? KeepPreviousData<TQueryData>(
        TQueryData? previousData,
        Query<TQueryData>? previousQuery) => previousData;

    /// <summary>
    /// Wraps a static value into a placeholder data delegate. The returned delegate
    /// is a single stable instance per call — capture it in a variable and reuse it
    /// if you need memoization to kick in.
    /// <code>
    /// var placeholder = QueryUtilities.Of("loading…");
    /// // reuse `placeholder` across option rebuilds for memoization
    /// </code>
    /// </summary>
    public static Func<TQueryData?, Query<TQueryData>?, TQueryData?> Of<TQueryData>(TQueryData value)
        => (_, _) => value;
}
