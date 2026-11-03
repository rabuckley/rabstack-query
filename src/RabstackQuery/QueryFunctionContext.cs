namespace RabstackQuery;

/// <summary>
/// Provides query function inputs. Reading <see cref="CancellationToken"/> enables
/// cancellation when the last observer unsubscribes.
/// </summary>
/// <remarks>
/// If the token is read, unsubscribing cancels the fetch and reverts its state.
/// Otherwise, retries stop but the current attempt can finish and populate the cache.
/// </remarks>
public sealed class QueryFunctionContext
{
    private readonly CancellationToken _cancellationToken;
    private readonly Action _onSignalConsumed;

    internal QueryFunctionContext(CancellationToken cancellationToken, Action onSignalConsumed)
    {
        _cancellationToken = cancellationToken;
        _onSignalConsumed = onSignalConsumed;
    }

    internal QueryFunctionContext(
        CancellationToken cancellationToken,
        Action onSignalConsumed,
        QueryClient client,
        QueryKey queryKey)
        : this(cancellationToken, onSignalConsumed)
    {
        Client = client;
        QueryKey = queryKey;
    }

    /// <summary>
    /// The <see cref="QueryClient"/> that owns the query. Used by
    /// <see cref="StreamedQuery"/> to call <c>SetQueryData</c> mid-fetch.
    /// Internal: regular user query functions should not call <c>SetQueryData</c>
    /// from inside a query function.
    /// </summary>
    internal QueryClient? Client { get; }

    /// <summary>
    /// The query key for the current query. Used by <see cref="StreamedQuery"/>
    /// to look up the query in the cache.
    /// </summary>
    internal QueryKey? QueryKey { get; }

    /// <summary>
    /// Cancellation token for the fetch operation. Accessing this property
    /// signals that the query function consumes cancellation — enabling
    /// automatic fetch cancellation when all observers unsubscribe.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get
        {
            _onSignalConsumed();
            return _cancellationToken;
        }
    }

    /// <summary>
    /// Raw token for infrastructure code that needs cancellation without
    /// triggering the signal-consumed flag (e.g., between-page cancellation
    /// checks in infinite queries).
    /// </summary>
    internal CancellationToken RawCancellationToken => _cancellationToken;

    /// <summary>
    /// The tracking callback, exposed so <see cref="InfiniteQueryFunctionContext{TPageParam}"/>
    /// can share the same flag.
    /// </summary>
    internal Action OnSignalConsumed => _onSignalConsumed;
}
