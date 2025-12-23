namespace RabstackQuery;

/// <summary>
/// Context passed to the infinite query function. Contains the page param to
/// fetch, the fetch direction, and a cancellation token. Mirrors the context
/// object TanStack passes to <c>queryFn</c> in <c>infiniteQueryBehavior.ts</c>.
/// </summary>
/// <remarks>
/// The <see cref="CancellationToken"/> getter shares the same tracking callback
/// as the parent <see cref="QueryFunctionContext"/>, so accessing it from the
/// user's query function correctly sets the abort-signal-consumed flag on the
/// owning query.
/// </remarks>
public sealed class InfiniteQueryFunctionContext<TPageParam>
{
    private readonly CancellationToken _cancellationToken;
    private readonly Action? _onSignalConsumed;

    internal InfiniteQueryFunctionContext(
        TPageParam pageParam,
        FetchDirection direction,
        CancellationToken cancellationToken,
        Action? onSignalConsumed)
    {
        PageParam = pageParam;
        Direction = direction;
        _cancellationToken = cancellationToken;
        _onSignalConsumed = onSignalConsumed;
    }

    /// <summary>The page param identifying which page to fetch.</summary>
    public TPageParam PageParam { get; }

    /// <summary>Whether this is a forward or backward page fetch.</summary>
    public FetchDirection Direction { get; }

    /// <summary>
    /// Cancellation token for the fetch operation. Accessing this property
    /// signals that the query function consumes cancellation — see
    /// <see cref="QueryFunctionContext.CancellationToken"/> for details.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get
        {
            _onSignalConsumed?.Invoke();
            return _cancellationToken;
        }
    }
}
