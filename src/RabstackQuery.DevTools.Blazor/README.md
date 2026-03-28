# RabStack Query DevTools for Blazor

A drop-in Blazor component that provides a real-time query and mutation inspector for [RabStack Query](https://www.nuget.org/packages/RabstackQuery), similar to [TanStack Query DevTools](https://tanstack.com/query/latest/docs/framework/react/devtools).

## Installation

```bash
dotnet add package RabstackQuery.DevTools.Blazor
```

## Usage

Add the component to your layout or page:

```razor
@using RabstackQuery.DevTools.Blazor

<RabstackQueryDevTools QueryClient="@QueryClient" />
```

With custom options:

```razor
<RabstackQueryDevTools QueryClient="@QueryClient"
                       Options="@(new DevToolsOptions
                       {
                           DataFormatter = data => JsonSerializer.Serialize(data)
                       })" />
```

## Features

- **Floating action button** with query count badge (turns red when queries are in error state)
- **Query list** with status indicators (Fresh, Stale, Fetching, Inactive, Error, Paused)
- **Mutation list** with status tracking
- **Detail views** for inspecting individual query/mutation state, data, and configuration
- **Search and sort** to find queries quickly
- **Live updates** via debounced cache observation

## Conditional Rendering

Include DevTools only in development builds:

```razor
@if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    <RabstackQueryDevTools QueryClient="@QueryClient" />
}
```

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
