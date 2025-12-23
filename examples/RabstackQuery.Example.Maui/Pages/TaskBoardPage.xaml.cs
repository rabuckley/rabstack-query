using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

[QueryProperty(nameof(ProjectId), "ProjectId")]
public partial class TaskBoardPage : ContentPage
{
    private readonly QueryClient _client;
    private readonly ITaskBoardApi _api;
    private TaskBoardViewModel? _viewModel;

    private int _projectId;

    public string? ProjectId
    {
        set
        {
            if (int.TryParse(value, out var id))
            {
                _projectId = id;
                InitializeViewModel(id);
            }
        }
    }

    public TaskBoardPage(QueryClient client, ITaskBoardApi api)
    {
        _client = client;
        _api = api;
        InitializeComponent();

        Unloaded += OnPageUnloaded;
    }

    private void InitializeViewModel(int projectId)
    {
        // Dispose previous ViewModel if the page is reused with a different project.
        _viewModel?.Dispose();

        _viewModel = new TaskBoardViewModel(_client, _api, projectId);
        BindingContext = _viewModel;
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        // The DataTemplate binds to TaskItem (the model record), not TaskItemViewModel.
        // Extract the task ID from the binding context of the tapped element.
        if (sender is BindableObject bindable && bindable.BindingContext is TaskItem task)
        {
            await Shell.Current.GoToAsync($"TaskDetail?ProjectId={_projectId}&TaskId={task.Id}");
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        Unloaded -= OnPageUnloaded;
        _viewModel?.Dispose();
    }
}
