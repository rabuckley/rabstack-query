namespace RabstackQuery.Example.Shared.Models;

public sealed record TaskItem
{
    public required int Id { get; init; }
    public required int ProjectId { get; init; }
    public required string Title { get; init; }
    public required string? Description { get; init; }
    public required TaskPriority Priority { get; init; }
    public required TaskItemStatus Status { get; init; }
    public required string? AssigneeName { get; init; }
    public required int CommentCount { get; init; }
    public required long CreatedAt { get; init; }
    public required long UpdatedAt { get; init; }
}
