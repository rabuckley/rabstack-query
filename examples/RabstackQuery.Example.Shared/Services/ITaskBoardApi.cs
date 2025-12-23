using RabstackQuery.Example.Shared.Models;

namespace RabstackQuery.Example.Shared.Services;

public interface ITaskBoardApi
{
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default);
    Task<Project> GetProjectAsync(int projectId, CancellationToken ct = default);
    Task<Project> CreateProjectAsync(string name, string description, CancellationToken ct = default);

    Task<PagedResult<TaskItem>> GetTasksAsync(int projectId, string? cursor = null,
        int pageSize = 20, CancellationToken ct = default);
    Task<TaskItem> GetTaskAsync(int projectId, int taskId, CancellationToken ct = default);
    Task<TaskItem> CreateTaskAsync(int projectId, string title, TaskPriority priority, CancellationToken ct = default);
    Task<TaskItem> UpdateTaskStatusAsync(int projectId, int taskId, TaskItemStatus status, CancellationToken ct = default);
    Task<TaskItem> UpdateTaskAsync(int projectId, int taskId, string title, string? description,
        TaskPriority priority, CancellationToken ct = default);
    Task DeleteTaskAsync(int projectId, int taskId, CancellationToken ct = default);

    Task<PagedResult<Comment>> GetCommentsAsync(int taskId, string? cursor = null,
        int pageSize = 10, CancellationToken ct = default);
    Task<Comment> AddCommentAsync(int taskId, string body, CancellationToken ct = default);
}
