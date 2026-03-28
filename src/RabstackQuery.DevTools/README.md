# RabStack Query DevTools

Core DevTools engine for [RabStack Query](https://www.nuget.org/packages/RabstackQuery). Observes cache events and builds displayable snapshots of query and mutation state.

This package provides the platform-agnostic `CacheObserver` and display models. For a ready-to-use UI, install one of:

- [`RabstackQuery.DevTools.Blazor`](https://www.nuget.org/packages/RabstackQuery.DevTools.Blazor) for Blazor apps
- [`RabstackQuery.DevTools.Maui`](https://www.nuget.org/packages/RabstackQuery.DevTools.Maui) for MAUI apps

## Installation

```bash
dotnet add package RabstackQuery.DevTools
```

## Usage

```csharp
var observer = new CacheObserver(queryClient, new DevToolsOptions());

observer.SnapshotsChanged += () =>
{
    foreach (var query in observer.Queries)
    {
        Console.WriteLine($"[{query.DisplayStatus}] {query.QueryHash} — observers: {query.ObserverCount}");
    }

    foreach (var mutation in observer.Mutations)
    {
        Console.WriteLine($"[{mutation.Status}] {mutation.MutationId}");
    }
};

// Don't forget to dispose when done
observer.Dispose();
```

## Key Types

| Type | Description |
|------|-------------|
| `CacheObserver` | Subscribes to `QueryCache` and `MutationCache` events, rebuilds snapshots on a debounced interval (250ms) |
| `QueryListItem` | Display model for a single query: hash, key, status, observer count, last update time |
| `MutationListItem` | Display model for a single mutation: ID, status, variables |
| `QueryDisplayStatus` | Status classification: `Fresh`, `Stale`, `Fetching`, `Paused`, `Inactive`, `Error` |
| `DevToolsOptions` | Configuration including `DataFormatter` (custom serialization for display) and `DevToolsColorValues` |
| `SortOption` | Built-in and custom sort configurations |

## Building a Custom UI

Use this package directly to build DevTools integrations for platforms beyond Blazor and MAUI. The `CacheObserver` exposes:

- `Queries` / `Mutations` — current snapshot lists
- `QueryCount` — total tracked queries
- `SnapshotsChanged` — event fired when snapshots are rebuilt
- `FindQueryByHash(string)` — look up the underlying `Query` for detail views
- `ForceRefresh()` — trigger an immediate snapshot rebuild

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
