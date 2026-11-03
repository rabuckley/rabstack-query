using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = _viewModel;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Subscribed = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Subscribed = false;
    }
}
