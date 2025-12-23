namespace RabstackQuery.Example.Shared.Models;

public sealed record Comment
{
    public required int Id { get; init; }
    public required int TaskId { get; init; }
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required long CreatedAt { get; init; }
}
