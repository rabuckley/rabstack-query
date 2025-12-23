using RabstackQuery.Example.Maui.Pages;

namespace RabstackQuery.Example.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // TaskBoardPage and TaskDetailPage are navigated to via Shell.Current.GoToAsync
        // and are not tab items, so they need explicit route registration.
        Routing.RegisterRoute("TaskBoard", typeof(TaskBoardPage));
        Routing.RegisterRoute("TaskDetail", typeof(TaskDetailPage));
    }
}
