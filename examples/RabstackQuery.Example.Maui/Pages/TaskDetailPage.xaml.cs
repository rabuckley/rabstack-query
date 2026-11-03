using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

public partial class TaskDetailPage : ContentPage, IQueryAttributable
{
    private readonly QueryClient _client;
    private readonly ITaskBoardApi _api;
    private TaskDetailViewModel? _viewModel;

    // The (ProjectId, TaskId) pair the current ViewModel was built for. Shell can
    // re-apply query attributes to this page instance, with new values or the same
    // ones, so the ViewModel is replaced only when the pair changes.
    private (int ProjectId, int TaskId)? _route;

    public TaskDetailPage(QueryClient client, ITaskBoardApi api)
    {
        _client = client;
        _api = api;
        InitializeComponent();
    }

    /// <summary>
    /// Receives ProjectId and TaskId together. [QueryProperty] setters arrive one at a
    /// time, which would build a ViewModel for a mixed pair (new project, old task)
    /// whenever the page is reused with new values.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!TryGetId(query, "ProjectId", out var projectId) || !TryGetId(query, "TaskId", out var taskId))
        {
            return;
        }

        if (_route == (projectId, taskId)) return;
        _route = (projectId, taskId);

        InitializeViewModel(projectId, taskId);
    }

    private static bool TryGetId(IDictionary<string, object> query, string key, out int id)
    {
        id = 0;
        return query.TryGetValue(key, out var value)
            && int.TryParse(value?.ToString(), out id)
            && id > 0;
    }

    private void InitializeViewModel(int projectId, int taskId)
    {
        _viewModel?.Dispose();
        var viewModel = new TaskDetailViewModel(_client, _api, projectId, taskId);
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Sync the status picker with the current task status once data is available.
        if (viewModel.TaskQuery.Data is { } task)
        {
            SyncStatusPicker(task.Status);
        }

        // Captures this ViewModel rather than reading _viewModel, so a late notification
        // from a replaced ViewModel cannot sync the picker from the new one's data.
        viewModel.TaskQuery.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Data" && viewModel.TaskQuery.Data is { } data
                && !viewModel.TaskQuery.IsPlaceholderData)
            {
                SyncStatusPicker(data.Status);
            }
        };
    }

    private void SyncStatusPicker(TaskItemStatus status)
    {
        var index = status switch
        {
            TaskItemStatus.Todo => 0,
            TaskItemStatus.InProgress => 1,
            TaskItemStatus.Review => 2,
            TaskItemStatus.Done => 3,
            _ => -1,
        };
        StatusPicker.SelectedIndex = index;
    }

    private async void OnStatusChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null || StatusPicker.SelectedIndex < 0) return;

        var newStatus = StatusPicker.SelectedIndex switch
        {
            0 => TaskItemStatus.Todo,
            1 => TaskItemStatus.InProgress,
            2 => TaskItemStatus.Review,
            3 => TaskItemStatus.Done,
            _ => TaskItemStatus.Todo,
        };

        // Only mutate if the status actually changed to avoid redundant API calls
        // (e.g. from the initial sync in InitializeViewModel).
        if (_viewModel.TaskQuery.Data?.Status != newStatus)
        {
            await _viewModel.UpdateStatusMutation.MutateCommand.ExecuteAsync(newStatus);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel is not null) _viewModel.Subscribed = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_viewModel is not null) _viewModel.Subscribed = false;
    }
}
