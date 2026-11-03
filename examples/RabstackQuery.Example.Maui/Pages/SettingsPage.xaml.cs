using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();
    }
}
