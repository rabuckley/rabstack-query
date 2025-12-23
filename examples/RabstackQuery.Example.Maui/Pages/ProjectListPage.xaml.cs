using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

public partial class ProjectListPage : ContentPage
{
    private readonly ProjectListViewModel _viewModel;

    public ProjectListPage(ProjectListViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = _viewModel;
        InitializeComponent();

        Unloaded += OnPageUnloaded;
    }

    private async void OnProjectTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int projectId)
        {
            await Shell.Current.GoToAsync($"TaskBoard?ProjectId={projectId}");
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        Unloaded -= OnPageUnloaded;
        _viewModel.Dispose();
    }
}
