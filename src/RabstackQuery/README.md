# RabStack Query

A .NET port of [TanStack Query](https://tanstack.com/query) providing declarative query and mutation management with automatic caching, background refetching, and optimistic updates.

## Installation

```bash
dotnet add package RabstackQuery
```

For MVVM bindings (MAUI, WPF, Avalonia), also install [`RabstackQuery.Mvvm`](https://www.nuget.org/packages/RabstackQuery.Mvvm). For Blazor, install [`RabstackQuery.Blazor`](https://www.nuget.org/packages/RabstackQuery.Blazor).

## Quick Start

```csharp
// Create a QueryClient (typically registered as a singleton)
var queryCache = new QueryCache();
var client = new QueryClient(queryCache);

// Fetch data with automatic caching
var result = await client.FetchQueryAsync(new FetchQueryOptions<List<Todo>>
{
    QueryKey = ["todos"],
    QueryFn = async ctx => await api.GetTodosAsync(ctx.CancellationToken)
});
```

## Features

- **Automatic Caching** with configurable stale time and garbage collection
- **Stale-While-Revalidate** serves cached data instantly while refetching in the background
- **Background Refetching** on window focus, network reconnection, and custom intervals
- **Retry with Exponential Backoff** (1s, 2s, 4s, 8s, capped at 30s) with configurable attempts
- **Mutations** with full lifecycle callbacks (`OnMutate`, `OnSuccess`, `OnError`, `OnSettled`)
- **Optimistic Updates** with automatic rollback on failure
- **Query Invalidation** with prefix matching for granular or bulk cache control
- **Reusable Query Definitions** via `QueryOptions<TData>` (analogous to TanStack v5's `queryOptions()`)
- **Observability** via `System.Diagnostics.Metrics` (18 instruments, opt-in via `IMeterFactory`)
- **Trim and AOT Compatible** with zero reflection in library code

## Core Concepts

### Query Keys

Query keys use C# 12 collection expressions for hierarchical cache identity:

```csharp
QueryKey key = ["todos"];                          // all todos
QueryKey key = ["todos", todoId];                  // single todo
QueryKey key = ["todos", new { status, page }];    // filtered list
```

### Query Observer

Subscribe to reactive query state updates:

```csharp
var observer = new QueryObserver<List<Todo>>(client, new QueryObserverOptions<List<Todo>>
{
    QueryKey = ["todos"],
    QueryFn = async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    StaleTime = TimeSpan.FromMinutes(5)
});

observer.Subscribe(result =>
{
    Console.WriteLine($"Status: {result.Status}, Items: {result.Data?.Count}");
});
```

### Select Transforms

Transform cached data without duplicating it:

```csharp
// Cache stores List<Todo>, observer returns only the count
var observer = new QueryObserver<int, List<Todo>>(client, new QueryObserverOptions<int, List<Todo>>
{
    QueryKey = ["todos"],
    QueryFn = async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    Select = todos => todos.Count
});
```

### Mutations with Optimistic Updates

```csharp
await client.MutateAsync(new MutationOptions<Todo, Exception, CreateTodoRequest, List<Todo>>
{
    MutationFn = async (request, _, ct) => await api.CreateTodoAsync(request, ct),
    OnMutate = async request =>
    {
        await client.CancelQueriesAsync(["todos"]);
        var previous = client.GetQueryData<List<Todo>>(["todos"]) ?? [];
        client.SetQueryData(["todos"], [.. previous, new Todo { Title = request.Title }]);
        return previous;  // context for rollback
    },
    OnError = (_, _, previous) =>
    {
        if (previous is not null) client.SetQueryData(["todos"], previous);
    },
    OnSettled = (_, _, _, _) => client.InvalidateQueries(["todos"])
},
new CreateTodoRequest { Title = "New todo" });
```

### Cache Operations

```csharp
// Manual cache reads and writes
client.SetQueryData(["user", userId], updatedUser);
var cached = client.GetQueryData<User>(["user", userId]);

// Invalidate all queries starting with "todos"
client.InvalidateQueries(["todos"]);

// Force refetch specific queries
client.RefetchQueries(["todos", "active"]);

// Remove queries from the cache
client.RemoveQueries(["todos", "deleted-item"]);
```

### Focus and Online Managers

```csharp
// Notify the library when the app gains/loses focus
FocusManager.Instance.SetFocused(true);

// Notify when network connectivity changes
OnlineManager.Instance.SetOnline(true);
```

## Configuration

```csharp
// Global defaults
client.SetDefaultOptions(new DefaultOptions
{
    Queries = new QueryConfiguration<object>
    {
        StaleTime = TimeSpan.FromMinutes(1),
        GcTime = TimeSpan.FromMinutes(10),
        Retry = 3,
        NetworkMode = NetworkMode.Online
    }
});

// Per-key defaults
client.SetQueryDefaults(["todos"], new QueryConfiguration<object>
{
    StaleTime = TimeSpan.FromMinutes(5)
});
```

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
