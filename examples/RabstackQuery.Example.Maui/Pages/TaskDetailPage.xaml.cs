using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

[QueryProperty(nameof(ProjectId), "ProjectId")]
[QueryProperty(nameof(TaskId), "TaskId")]
public partial class TaskDetailPage : ContentPage
{
    private readonly QueryClient _client;
    private readonly ITaskBoardApi _api;
    private TaskDetailViewModel? _viewModel;

    private int _projectId;
    private int _taskId;
    private bool _initialized;

    public string? ProjectId
    {
        set
        {
            if (int.TryParse(value, out var id))
            {
                _projectId = id;
                TryInitializeViewModel();
            }
        }
    }

    public string? TaskId
    {
        set
        {
            if (int.TryParse(value, out var id))
            {
                _taskId = id;
                TryInitializeViewModel();
            }
        }
    }

    public TaskDetailPage(QueryClient client, ITaskBoardApi api)
    {
        _client = client;
        _api = api;
        InitializeComponent();

        Unloaded += OnPageUnloaded;
    }

    /// <summary>
    /// Both ProjectId and TaskId must be set before creating the ViewModel.
    /// Shell sets query properties one at a time, so we wait until both are available.
    /// </summary>
    private void TryInitializeViewModel()
    {
        if (_projectId <= 0 || _taskId <= 0 || _initialized) return;
        _initialized = true;

        _viewModel?.Dispose();
        _viewModel = new TaskDetailViewModel(_client, _api, _projectId, _taskId);
        BindingContext = _viewModel;

        // Sync the status picker with the current task status once data is available.
        if (_viewModel.TaskQuery.Data is { } task)
        {
            SyncStatusPicker(task.Status);
        }

        _viewModel.TaskQuery.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Data" && _viewModel.TaskQuery.Data is { } data
                && !_viewModel.TaskQuery.IsPlaceholderData)
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
        // (e.g. from the initial sync in TryInitializeViewModel).
        if (_viewModel.TaskQuery.Data?.Status != newStatus)
        {
            await _viewModel.UpdateStatusMutation.MutateCommand.ExecuteAsync(newStatus);
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        Unloaded -= OnPageUnloaded;
        _viewModel?.Dispose();
    }
}
