namespace RabstackQuery;

public sealed class SetStateAction<TData> : DispatchAction
{
    public required QueryState<TData> State { get; init; }
    public required SetStateOptions? Options { get; init; }
}
