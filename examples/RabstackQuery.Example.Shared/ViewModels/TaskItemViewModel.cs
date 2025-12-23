using CommunityToolkit.Mvvm.ComponentModel;

using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Mvvm;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// ViewModel for individual task items with observable properties for UI binding.
/// Manages optimistic mutations for status changes, priority toggling, and deletion.
/// </summary>
public sealed partial class TaskItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    public partial int Id { get; set; }

    [ObservableProperty]
    public partial int ProjectId { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial TaskPriority Priority { get; set; }

    [ObservableProperty]
    public partial TaskItemStatus Status { get; set; }

    [ObservableProperty]
    public partial string? AssigneeName { get; set; }

    [ObservableProperty]
    public partial int CommentCount { get; set; }

    [ObservableProperty]
    public partial long CreatedAt { get; set; }

    [ObservableProperty]
    public partial long UpdatedAt { get; set; }

    public MutationViewModel<TaskItem, Exception, TaskItemStatus, TaskItemStatus> StatusMutation { get; }

    public MutationViewModel<TaskItem, Exception, TaskPriority, TaskPriority> TogglePriorityMutation { get; }

    public MutationViewModel<object?, int> DeleteMutation { get; }

    /// <summary>
    /// Creates a TaskItemViewModel from a TaskItem model.
    /// </summary>
    public static TaskItemViewModel FromTaskItem(TaskItem task, QueryClient client, ITaskBoardApi api)
    {
        return new TaskItemViewModel(client, api)
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Priority = task.Priority,
            Status = task.Status,
            AssigneeName = task.AssigneeName,
            CommentCount = task.CommentCount,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
        };
    }

    private TaskItemViewModel(QueryClient client, ITaskBoardApi api)
    {
        // Optimistic status update — saves old status as context for rollback on error.
        StatusMutation = client.UseMutation<TaskItem, Exception, TaskItemStatus, TaskItemStatus>(
            mutationFn: async (newStatus, context, ct) =>
                await api.UpdateTaskStatusAsync(ProjectId, Id, newStatus, ct),
            options: new()
            {
                OnMutate = async (newStatus, context) =>
                {
                    var oldStatus = Status;
                    Status = newStatus;
                    return oldStatus;
                },
                OnError = async (error, variables, oldStatus, context) =>
                {
                    if (oldStatus is { } previous)
                    {
                        Status = previous;
                    }
                },
                OnSuccess = async (data, variables, oldStatus, context) =>
                {
                    UpdateAllProperties(data);
                },
            }
        );

        // Optimistic priority cycling (Low -> Medium -> High -> Urgent -> Low).
        // The mutation variable is the desired new priority; the context stores the old
        // priority for rollback on error.
        TogglePriorityMutation = client.UseMutation<TaskItem, Exception, TaskPriority, TaskPriority>(
            mutationFn: async (newPriority, context, ct) =>
                await api.UpdateTaskAsync(ProjectId, Id, Title, Description, newPriority, ct),
            options: new()
            {
                OnMutate = async (newPriority, context) =>
                {
                    var oldPriority = Priority;
                    Priority = newPriority;
                    return oldPriority;
                },
                OnError = async (error, variables, oldPriority, context) =>
                {
                    if (oldPriority is { } previous)
                    {
                        Priority = previous;
                    }
                },
                OnSuccess = async (data, variables, oldPriority, context) =>
                {
                    UpdateAllProperties(data);
                },
            }
        );

        // Deletion mutation — invalidates the tasks query on success so the list refreshes.
        DeleteMutation = client.UseMutation<object?, int>(
            mutationFn: async (_, context, ct) =>
            {
                await api.DeleteTaskAsync(ProjectId, Id, ct);
                return null;
            },
            options: new()
            {
                OnSuccess = async (data, variables, onMutateResult, context) =>
                {
                    await context.Client.InvalidateQueries(QueryKeys.Tasks(ProjectId));
                },
            }
        );
    }

    /// <summary>
    /// Converts back to an immutable TaskItem record.
    /// </summary>
    public TaskItem ToTaskItem()
    {
        return new TaskItem
        {
            Id = Id,
            ProjectId = ProjectId,
            Title = Title,
            Description = Description,
            Priority = Priority,
            Status = Status,
            AssigneeName = AssigneeName,
            CommentCount = CommentCount,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }

    /// <summary>
    /// Gets a human-readable creation date.
    /// </summary>
    public string FormattedCreatedAt =>
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("g");

    /// <summary>
    /// Updates all observable properties from a server-returned TaskItem.
    /// </summary>
    private void UpdateAllProperties(TaskItem task)
    {
        Id = task.Id;
        ProjectId = task.ProjectId;
        Title = task.Title;
        Description = task.Description;
        Priority = task.Priority;
        Status = task.Status;
        AssigneeName = task.AssigneeName;
        CommentCount = task.CommentCount;
        CreatedAt = task.CreatedAt;
        UpdatedAt = task.UpdatedAt;
    }

    public void Dispose()
    {
        StatusMutation.Dispose();
        TogglePriorityMutation.Dispose();
        DeleteMutation.Dispose();
        GC.SuppressFinalize(this);
    }
}
