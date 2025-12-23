namespace RabstackQuery;

public sealed class ErrorAction<TData> : DispatchAction
{
    public required Exception Error { get; init; }
}
