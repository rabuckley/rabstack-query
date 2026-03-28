# RabStack Query DevTools for MAUI

A floating debug overlay for .NET MAUI apps that provides real-time visibility into [RabStack Query](https://www.nuget.org/packages/RabstackQuery) cache state, similar to [TanStack Query DevTools](https://tanstack.com/query/latest/docs/framework/react/devtools).

## Installation

```bash
dotnet add package RabstackQuery.DevTools.Maui
```

## Supported Platforms

- Android (API 21+)
- iOS (15.0+)
- Mac Catalyst (15.0+)
- Windows (10.0.17763+)

## Usage

Attach DevTools to a window, typically in your `App` class:

```csharp
using RabstackQuery.DevTools.Maui;

public class App : Application
{
    private readonly QueryClient _queryClient;

    public App(QueryClient queryClient) => _queryClient = queryClient;

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage());

#if DEBUG
        window.UseRabstackQueryDevTools(_queryClient);
#endif

        return window;
    }
}
```

With custom options:

```csharp
window.UseRabstackQueryDevTools(_queryClient, new DevToolsOptions
{
    DataFormatter = data => JsonSerializer.Serialize(data, AppJsonContext.Default.Options)
});
```

## Features

- **Floating "RQ" button** in the bottom-right corner with a badge showing query count
- **Badge color** turns red when any query is in error state
- **Modal inspector** opens on tap with full query/mutation list, search, sort, and detail views
- **Theme-aware** adapts to light and dark mode
- **Window lifecycle** the observer is automatically disposed when the window is destroyed

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
