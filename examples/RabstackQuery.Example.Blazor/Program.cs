using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using RabstackQuery;
using RabstackQuery.Example.Blazor;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Example.Shared.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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
});

// Mock API layer
builder.Services.AddSingleton<MockApiSettings>();
builder.Services.AddSingleton<MockTaskBoardApi>();
builder.Services.AddSingleton<ITaskBoardApi>(sp => sp.GetRequiredService<MockTaskBoardApi>());

// ViewModels — transient so each page navigation gets a fresh instance.
builder.Services.AddTransient<DashboardViewModel>();
builder.Services.AddTransient<ProjectListViewModel>();
builder.Services.AddTransient<SettingsViewModel>();

await builder.Build().RunAsync();
