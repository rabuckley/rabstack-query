using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;

namespace RabstackQuery.Example.Shared;

/// <summary>
/// Typed query definitions using <see cref="QueryOptions{TData}"/>.
/// Each method returns a reusable object that bundles key + queryFn + config,
/// with <c>TData</c> inferred from the query function. These can be passed to
/// <c>UseQuery</c>, <c>FetchQueryAsync</c>, <c>GetQueryData</c>, and other APIs
/// that accept <see cref="QueryOptions{TData}"/>.
/// <para>
/// <see cref="QueryKeys"/> remains the source of truth for key structure.
/// This class adds the query function and shared configuration on top.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Define once
/// var opts = Queries.Projects(api);
///
/// // Observe reactively
/// ProjectsQuery = client.UseQuery(opts);
///
/// // Read cache imperatively (TData inferred)
/// var cached = client.GetQueryData(opts);
///
/// // Write cache imperatively
/// client.SetQueryData(opts, updatedProjects);
///
/// // Prefetch in background
/// await client.PrefetchQueryAsync(opts);
/// </code>
/// </example>
public static class Queries
{
    public static QueryOptions<IEnumerable<Project>> Projects(ITaskBoardApi api) => new()
    {
        QueryKey = QueryKeys.Projects,
        QueryFn = async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
        StaleTime = TimeSpan.FromSeconds(60),
    };

    public static QueryOptions<PagedResult<TaskItem>> Tasks(ITaskBoardApi api, int projectId) => new()
    {
        QueryKey = QueryKeys.Tasks(projectId),
        QueryFn = async ctx => await api.GetTasksAsync(projectId, ct: ctx.CancellationToken),
    };

    public static QueryOptions<TaskItem> Task(ITaskBoardApi api, int projectId, int taskId) => new()
    {
        QueryKey = QueryKeys.Task(projectId, taskId),
        QueryFn = async ctx => await api.GetTaskAsync(projectId, taskId, ctx.CancellationToken),
    };
}
