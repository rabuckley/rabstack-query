using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Mvvm;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// ViewModel for the Task detail page demonstrating:
/// - <b>PlaceholderData</b> seeded from the infinite query list cache for instant perceived load
/// - <b>Dependent query (Enabled flag)</b> that only fetches when TaskId > 0
/// - <b>IsPlaceholderData</b> visual indicator
/// - <b>Cross-query cache update</b> via SetQueryData in mutation OnSuccess
/// - <b>Infinite query</b> for paginated comments with reverse pagination (newest first)
/// - <b>Optimistic status update</b> with detail + list cache invalidation
/// </summary>
public sealed partial class TaskDetailViewModel : ObservableObject, IDisposable
{
    private readonly QueryClient _client;
    private readonly ITaskBoardApi _api;

    public int ProjectId { get; }
    public int TaskId { get; }

    public QueryViewModel<TaskItem> TaskQuery { get; }

    [ObservableProperty]
    public partial string EditableTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EditableDescription { get; set; }

    public MutationViewModel<TaskItem, (string Title, string? Description, TaskPriority Priority)> UpdateTaskMutation { get; }

    public MutationViewModel<TaskItem, TaskItemStatus> UpdateStatusMutation { get; }

    // ── Comments ─────────────────────────────────────────────────────────

    public InfiniteQueryViewModel<PagedResult<Comment>, string?> CommentsQuery { get; }

    public MutationViewModel<Comment, string> AddCommentMutation { get; }

    [ObservableProperty]
    public partial string NewCommentText { get; set; } = string.Empty;

    public TaskDetailViewModel(QueryClient client, ITaskBoardApi api, int projectId, int taskId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(api);

        _client = client;
        _api = api;
        ProjectId = projectId;
        TaskId = taskId;

        // -- Task detail query with placeholder data from infinite list cache --

        TaskQuery = client.UseQuery(new QueryObserverOptions<TaskItem>
        {
            QueryKey = QueryKeys.Task(projectId, taskId),
            QueryFn = async ctx => await api.GetTaskAsync(projectId, taskId, ctx.CancellationToken),
            Enabled = taskId > 0,

            // Seed placeholder data from the infinite query list cache so the UI
            // shows something instantly while the detail fetch is in flight.
            PlaceholderData = (_, _) =>
            {
                var infiniteData = client.GetQueryData<InfiniteData<PagedResult<TaskItem>, string?>>(
                    QueryKeys.Tasks(projectId));
                return infiniteData?.Pages
                    .SelectMany(p => p.Items)
                    .FirstOrDefault(t => t.Id == taskId);
            }
        });

        // Sync editable fields when the query result changes. We subscribe to
        // PropertyChanged rather than polling so the fields update as soon as real
        // data arrives (replacing placeholder data).
        TaskQuery.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(QueryViewModel<TaskItem>.Data) && TaskQuery.Data is { } data)
            {
                if (!TaskQuery.IsPlaceholderData)
                {
                    EditableTitle = data.Title;
                    EditableDescription = data.Description;
                }
            }
        };

        // Initialize from current data (may be placeholder)
        if (TaskQuery.Data is { } initial)
        {
            EditableTitle = initial.Title;
            EditableDescription = initial.Description;
        }

        // -- Update task mutation (title, description, priority) ──────────

        UpdateTaskMutation = client.UseMutation<TaskItem, (string Title, string? Description, TaskPriority Priority)>(
            mutationFn: async (variables, context, ct) =>
                await api.UpdateTaskAsync(projectId, taskId, variables.Title, variables.Description, variables.Priority, ct),
            options: new()
            {
                OnSuccess = async (updatedTask, _, _, context) =>
                {
                    // Update the detail cache with the server response
                    context.Client.SetQueryData(QueryKeys.Task(projectId, taskId), updatedTask);

                    // Invalidate the task list so it refetches with the updated data
                    await context.Client.InvalidateQueries(QueryKeys.Tasks(projectId));
                }
            }
        );

        // -- Optimistic status update mutation ────────────────────────────

        UpdateStatusMutation = client.UseMutation<TaskItem, TaskItemStatus>(
            mutationFn: async (status, context, ct) =>
                await api.UpdateTaskStatusAsync(projectId, taskId, status, ct),
            options: new()
            {
                OnMutate = async (newStatus, context) =>
                {
                    // Cancel in-flight queries so they don't overwrite our optimistic update
                    await context.Client.CancelQueriesAsync(QueryKeys.Task(projectId, taskId));

                    // Optimistically update the detail cache
                    var previousTask = context.Client.GetQueryData<TaskItem>(QueryKeys.Task(projectId, taskId));
                    if (previousTask is not null)
                    {
                        context.Client.SetQueryData(QueryKeys.Task(projectId, taskId),
                            previousTask with { Status = newStatus });
                    }

                    return null;
                },
                OnSuccess = async (updatedTask, _, _, context) =>
                {
                    // Replace the optimistic data with the server response
                    context.Client.SetQueryData(QueryKeys.Task(projectId, taskId), updatedTask);

                    // Invalidate the task list to reflect the status change
                    await context.Client.InvalidateQueries(QueryKeys.Tasks(projectId));
                },
                OnError = async (_, _, _, context) =>
                {
                    // Roll back the optimistic update by refetching
                    await context.Client.InvalidateQueries(QueryKeys.Task(projectId, taskId));
                }
            }
        );

        // -- Paginated comments (reverse chronological, newest first) ─────

        CommentsQuery = client.UseInfiniteQuery(new InfiniteQueryObserverOptions<PagedResult<Comment>, string?>
        {
            QueryKey = QueryKeys.Comments(taskId),
            QueryFn = async ctx => await api.GetCommentsAsync(taskId, ctx.PageParam, 10, ctx.CancellationToken),
            InitialPageParam = null,
            GetNextPageParam = ctx => ctx.Page.NextCursor is { } cursor
                ? PageParamResult<string?>.Some(cursor)
                : PageParamResult<string?>.None,
            Enabled = taskId > 0,
            StaleTime = TimeSpan.FromSeconds(30),
        });

        // -- Add comment mutation ─────────────────────────────────────────

        AddCommentMutation = client.UseMutation<Comment, string>(
            mutationFn: async (body, context, ct) =>
                await api.AddCommentAsync(taskId, body, ct),
            options: new()
            {
                OnSuccess = async (_, _, _, context) =>
                {
                    await context.Client.InvalidateQueries(QueryKeys.Comments(taskId));
                }
            }
        );
    }

    [RelayCommand]
    private async Task UpdateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(EditableTitle) || TaskId <= 0)
        {
            return;
        }

        var priority = TaskQuery.Data?.Priority ?? TaskPriority.Medium;
        await UpdateTaskMutation.MutateCommand.ExecuteAsync((EditableTitle, EditableDescription, priority));
    }

    [RelayCommand]
    private async Task AddCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCommentText) || TaskId <= 0)
        {
            return;
        }

        await AddCommentMutation.MutateCommand.ExecuteAsync(NewCommentText);

        if (AddCommentMutation.IsSuccess)
        {
            NewCommentText = string.Empty;
        }
    }

    public void Dispose()
    {
        TaskQuery.Dispose();
        UpdateTaskMutation.Dispose();
        UpdateStatusMutation.Dispose();
        CommentsQuery.Dispose();
        AddCommentMutation.Dispose();
    }
}
