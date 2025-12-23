namespace RabstackQuery.Example.Maui;

using System.Text.Json;

using RabstackQuery;
#if DEBUG && (ANDROID || IOS || MACCATALYST || WINDOWS)
using RabstackQuery.DevTools;
using RabstackQuery.DevTools.Maui;
#endif

public sealed partial class App : Application
{
#if DEBUG && (ANDROID || IOS || MACCATALYST || WINDOWS)
    private readonly QueryClient _queryClient;

    public App(QueryClient queryClient)
    {
        _queryClient = queryClient;
        RegisterLifecycleEvents();
    }
#else
    public App()
    {
        RegisterLifecycleEvents();
    }
#endif

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // Wire focus tracking to Window lifecycle events. Deactivated fires when
        // the app loses focus (user switches away); Activated fires when it
        // regains focus.
        window.Activated += (_, _) => FocusManager.Instance.SetFocused(true);
        window.Deactivated += (_, _) => FocusManager.Instance.SetFocused(false);

#if DEBUG && (ANDROID || IOS || MACCATALYST || WINDOWS)
        window.UseRabstackQueryDevTools(_queryClient, new DevToolsOptions
        {
            DataFormatter = data => JsonSerializer.Serialize(data, data?.GetType() ?? typeof(object), s_jsonOptions),
        });
#endif

        return window;
    }

#if DEBUG && (ANDROID || IOS || MACCATALYST || WINDOWS)
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
#endif

    private static void RegisterLifecycleEvents()
    {
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    private static void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        OnlineManager.Instance.SetOnline(e.NetworkAccess is NetworkAccess.Internet);
    }
}
