namespace RabstackQuery;

public sealed class FailedAction<TData> : DispatchAction
{
    public required int FailureCount { get; init; }
    public required Exception Error { get; init; }
}
