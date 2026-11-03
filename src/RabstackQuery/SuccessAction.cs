namespace RabstackQuery;

internal sealed class SuccessAction<TData> : DispatchAction
{
    public required TData Data { get; init; }

    public required long DataUpdatedAt { get; init; }

    public required bool Manual { get; init; }
}
