namespace RabstackQuery;

/// <summary>
/// The internal mutable state of a <see cref="Query{TData}"/>, tracking data,
/// error, fetch status, update counts, and invalidation.
/// </summary>
public sealed record QueryState<TData>
{
    public TData? Data { get; init; }

    public int DataUpdateCount { get; init; }

    public long DataUpdatedAt { get; init; }

    public Exception? Error { get; init; }

    public int ErrorUpdateCount { get; init; }

    public long ErrorUpdatedAt { get; init; }

    public int FetchFailureCount { get; init; }

    public Exception? FetchFailureReason { get; init; }

    public FetchMeta? FetchMeta { get; init; }

    public bool IsInvalidated { get; init; }

    public QueryStatus Status { get; init; }

    public FetchStatus FetchStatus { get; init; }
}
