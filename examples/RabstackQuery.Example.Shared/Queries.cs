using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;

namespace RabstackQuery.Example.Shared;

public static class Queries
{
    /// <summary>
    /// Project metadata changes infrequently (names, descriptions, counts).
    /// </summary>
    public static QueryOptions<IEnumerable<Project>> Projects(ITaskBoardApi api) => new()
    {
        QueryKey = QueryKeys.Projects,
        QueryFn = async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
        StaleTime = TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// Task lists are collaborative — status changes and new tasks arrive frequently.
    /// </summary>
    public static QueryOptions<PagedResult<TaskItem>> Tasks(ITaskBoardApi api, int projectId) => new()
    {
        QueryKey = QueryKeys.Tasks(projectId),
        QueryFn = async ctx => await api.GetTasksAsync(projectId, ct: ctx.CancellationToken),
        StaleTime = TimeSpan.FromSeconds(15),
    };

    /// <summary>
    /// Individual task being actively viewed — keep it fresh.
    /// </summary>
    public static QueryOptions<TaskItem> Task(ITaskBoardApi api, int projectId, int taskId) => new()
    {
        QueryKey = QueryKeys.Task(projectId, taskId),
        QueryFn = async ctx => await api.GetTaskAsync(projectId, taskId, ctx.CancellationToken),
        StaleTime = TimeSpan.FromSeconds(15),
    };
}
