namespace RabstackQuery;

/// <summary>
/// Controls how <see cref="StreamedQuery"/> handles data when a query refetches.
/// Mirrors TanStack's <c>refetchMode</c> parameter on <c>streamedQuery()</c>.
/// </summary>
public enum StreamRefetchMode
{
    /// <summary>
    /// Clears cached data and sets the query back to pending state before streaming.
    /// This is the default behavior.
    /// </summary>
    Reset,

    /// <summary>
    /// Keeps existing cached data and appends new chunks from the refetch stream.
    /// </summary>
    Append,

    /// <summary>
    /// Buffers chunks locally during the refetch and writes all data to the cache
    /// only when the stream completes. Existing data remains visible until then.
    /// </summary>
    Replace
}
