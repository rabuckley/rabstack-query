using CommunityToolkit.Maui;

using MauiDevFlow.Agent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RabstackQuery;
using RabstackQuery.Example.Maui.Pages;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Example.Shared.ViewModels;

namespace RabstackQuery.Example.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiCommunityToolkit()
            .UseMauiApp<App>();

#if DEBUG
        builder.Logging
            .AddDebug()
            .SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif

        // ILoggerFactory is resolved automatically from DI by AddRabstackQuery.
        // MAUI uses Singleton because there is one user per app instance.
        builder.Services.AddRabstackQuery(options =>
        {
            options.DefaultOptions = new QueryClientDefaultOptions
            {
                StaleTime = TimeSpan.FromSeconds(30),
                Retry = 2,
            };
            options.SetQueryDefaults(new QueryDefaults
            {
                QueryKey = ["projects"],
                GcTime = TimeSpan.FromMinutes(10),
            });
            options.SetQueryDefaults(new QueryDefaults
            {
                QueryKey = ["tasks"],
                StaleTime = TimeSpan.FromSeconds(10),
            });
        }, ServiceLifetime.Singleton);

        // Register application services
        builder.Services.AddSingleton<MockApiSettings>();
        builder.Services.AddSingleton<MockTaskBoardApi>();
        builder.Services.AddSingleton<ITaskBoardApi>(sp => sp.GetRequiredService<MockTaskBoardApi>());

        // Register ViewModels (Transient — new instance per navigation)
        // TaskBoardViewModel and TaskDetailViewModel are constructed manually in their
        // page code-behinds because they require ProjectId/TaskId parameters that are
        // only known at navigation time.
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<ProjectListViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Register Pages (Transient)
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<ProjectListPage>();
        builder.Services.AddTransient<TaskBoardPage>();
        builder.Services.AddTransient<TaskDetailPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
