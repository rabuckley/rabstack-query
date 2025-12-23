using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Mvvm;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// Centerpiece ViewModel for the task board page demonstrating infinite queries,
/// optimistic mutations, and client-side grouping by task status. Tasks are fetched
/// with cursor-based pagination and grouped into swim lanes (Todo, InProgress, Review, Done).
/// </summary>
public sealed partial class TaskBoardViewModel : ObservableObject, IDisposable
{
    private readonly QueryClient _client;
    private bool _disposed;

    public int ProjectId { get; }

    public InfiniteQueryViewModel<PagedResult<TaskItem>, string?> TasksQuery { get; }

    public QueryViewModel<Project>? ProjectQuery { get; }

    public MutationViewModel<TaskItem, (string Title, TaskPriority Priority)> AddTaskMutation { get; }

    [ObservableProperty]
    public partial string NewTaskTitle { get; set; } = string.Empty;

    // ── Computed properties: tasks grouped by status ─────────────────────

    [ObservableProperty]
    public partial IReadOnlyList<TaskItem> TodoTasks { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TaskItem> InProgressTasks { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TaskItem> ReviewTasks { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TaskItem> DoneTasks { get; private set; } = [];

    [ObservableProperty]
    public partial int PendingMutationCount { get; private set; }

    public TaskBoardViewModel(QueryClient client, ITaskBoardApi api, int projectId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(api);

        _client = client;
        ProjectId = projectId;

        // Cursor-based infinite query for tasks in this project.
        TasksQuery = client.UseInfiniteQuery(new InfiniteQueryObserverOptions<PagedResult<TaskItem>, string?>
        {
            QueryKey = QueryKeys.Tasks(projectId),
            QueryFn = async ctx =>
                await api.GetTasksAsync(projectId, ctx.PageParam, 20, ctx.CancellationToken),
            InitialPageParam = null,
            GetNextPageParam = ctx => ctx.Page.NextCursor is { } cursor
                ? PageParamResult<string?>.Some(cursor)
                : PageParamResult<string?>.None,
            StaleTime = TimeSpan.FromSeconds(10),
        });

        // Project details query with placeholder data seeded from the projects list
        // cache so the page title renders instantly while the detail fetch is in flight.
        ProjectQuery = client.UseQuery(new QueryObserverOptions<Project>
        {
            QueryKey = QueryKeys.Project(projectId),
            QueryFn = async ctx => await api.GetProjectAsync(projectId, ctx.CancellationToken),
            PlaceholderData = (_, _) =>
            {
                var projects = client.GetQueryData<IEnumerable<Project>>(QueryKeys.Projects);
                return projects?.FirstOrDefault(p => p.Id == projectId);
            },
        });

        AddTaskMutation = client.UseMutation<TaskItem, (string Title, TaskPriority Priority)>(
            mutationFn: async (variables, context, ct) =>
                await api.CreateTaskAsync(projectId, variables.Title, variables.Priority, ct),
            options: new()
            {
                OnSuccess = async (data, variables, onMutateResult, context) =>
                {
                    // Refresh the task list and project details (task counts may have changed).
                    await context.Client.InvalidateQueries(QueryKeys.Tasks(projectId));
                    await context.Client.InvalidateQueries(QueryKeys.Projects);
                },
            }
        );

        // Recompute the grouped task lists whenever the infinite query data changes.
        TasksQuery.PropertyChanged += OnTasksQueryPropertyChanged;

        // Seed groupings from any data already present (e.g. from cache).
        UpdateGroupedTasks();
    }

    private void OnTasksQueryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InfiniteQueryViewModel<PagedResult<TaskItem>, string?>.Data))
        {
            UpdateGroupedTasks();
        }

        if (args.PropertyName is nameof(InfiniteQueryViewModel<PagedResult<TaskItem>, string?>.IsFetching))
        {
            // Use IsFetching as a proxy for pending mutation count — this gives the UI
            // a badge to show when background work is in progress.
            PendingMutationCount = TasksQuery.IsFetching ? 1 : 0;
        }
    }

    /// <summary>
    /// Flattens all loaded pages and groups tasks into status-based swim lanes.
    /// </summary>
    private void UpdateGroupedTasks()
    {
        var allTasks = TasksQuery.Data?.Pages
            .SelectMany(p => p.Items)
            .ToList();

        if (allTasks is null or { Count: 0 })
        {
            TodoTasks = [];
            InProgressTasks = [];
            ReviewTasks = [];
            DoneTasks = [];
            return;
        }

        TodoTasks = allTasks.Where(t => t.Status is TaskItemStatus.Todo).ToList();
        InProgressTasks = allTasks.Where(t => t.Status is TaskItemStatus.InProgress).ToList();
        ReviewTasks = allTasks.Where(t => t.Status is TaskItemStatus.Review).ToList();
        DoneTasks = allTasks.Where(t => t.Status is TaskItemStatus.Done).ToList();
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
        {
            return;
        }

        await AddTaskMutation.MutateCommand.ExecuteAsync((NewTaskTitle, TaskPriority.Medium));

        if (AddTaskMutation.IsSuccess)
        {
            NewTaskTitle = string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TasksQuery.PropertyChanged -= OnTasksQueryPropertyChanged;
        TasksQuery.Dispose();
        ProjectQuery?.Dispose();
        AddTaskMutation.Dispose();
    }
}
