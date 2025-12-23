namespace RabstackQuery.Example.Shared;

/// <summary>
/// Hierarchical query key factory for the task board domain.
/// <c>InvalidateQueries(["projects", projectId])</c> cascades to all
/// tasks under that project via prefix matching.
/// </summary>
public static class QueryKeys
{
    public static QueryKey Projects => ["projects"];
    public static QueryKey Project(int id) => ["projects", id];
    public static QueryKey Tasks(int projectId) => ["projects", projectId, "tasks"];
    public static QueryKey Task(int projectId, int taskId) => ["projects", projectId, "tasks", taskId];
    public static QueryKey Comments(int taskId) => ["tasks", taskId, "comments"];
}
