namespace RabstackQuery.Example.Shared.Models;

public sealed record Project
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Color { get; init; }
    public required int TaskCount { get; init; }
    public required int CompletedTaskCount { get; init; }
    public required long CreatedAt { get; init; }
}
