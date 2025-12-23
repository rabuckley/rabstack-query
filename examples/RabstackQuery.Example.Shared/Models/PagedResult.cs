namespace RabstackQuery.Example.Shared.Models;

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required string? NextCursor { get; init; }
    public required int TotalCount { get; init; }
}
