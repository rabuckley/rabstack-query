# RabStack Query

A powerful, type-safe data synchronization library for .NET, inspired by [TanStack Query](https://tanstack.com/query) (React Query). RabStack Query provides declarative query and mutation management with automatic caching, background refetching, and optimistic updates for MAUI, Blazor, and other .NET applications.

## Features

- **Automatic Caching** with configurable stale time and garbage collection
- **Stale-While-Revalidate** — serve cached data instantly while refetching in the background
- **Background Refetching** on window focus, network reconnection, and polling intervals
- **Optimistic Updates** with automatic rollback on error
- **Mutations** with lifecycle hooks (onMutate, onSuccess, onError, onSettled)
- **Infinite Queries** for cursor-based pagination
- **Hierarchical Query Keys** with prefix-based invalidation
- **MVVM Bindings** — `QueryViewModel`, `MutationViewModel`, `InfiniteQueryViewModel` with `INotifyPropertyChanged` and `IAsyncRelayCommand`
- **Retry with Exponential Backoff** (1s, 2s, 4s, 8s, max 30s)
- **Trim and AOT Safe** — no reflection, `IsTrimmable` and `IsAotCompatible`

## Installation

```bash
# Core library
dotnet add package RabstackQuery

# MVVM bindings for MAUI/Blazor
dotnet add package RabstackQuery.Mvvm
```

## Quick Start

Register the `QueryClient` in DI:

```csharp
builder.Services.AddRabstackQuery(options =>
{
    options.DefaultOptions = new QueryClientDefaultOptions
    {
        StaleTime = TimeSpan.FromSeconds(30),
        Retry = 2,
    };
});
```

Use `Scoped` (default) for Blazor, `Singleton` for MAUI:

```csharp
builder.Services.AddRabstackQuery(configure: _ => { }, ServiceLifetime.Singleton);
```

## Queries

### Basic Query

`UseQuery` creates a `QueryViewModel` that fetches data, caches it, and exposes reactive properties (`Data`, `IsLoading`, `IsError`, `Error`, `IsSuccess`, `IsStale`) via `INotifyPropertyChanged`:

```csharp
public sealed class TodosViewModel : IDisposable
{
    public QueryViewModel<List<Todo>> TodosQuery { get; }

    public TodosViewModel(QueryClient client, ITodoApi api)
    {
        TodosQuery = client.UseQuery(
            queryKey: ["todos"],
            queryFn: async ctx => await api.GetTodosAsync(ctx.CancellationToken)
        );
    }

    public void Dispose() => TodosQuery.Dispose();
}
```

Bind directly to the ViewModel properties in XAML or Blazor:

```xml
<ActivityIndicator IsRunning="{Binding TodosQuery.IsLoading}" />
<Label Text="{Binding TodosQuery.Error.Message}" IsVisible="{Binding TodosQuery.IsError}" />
<CollectionView ItemsSource="{Binding TodosQuery.Data}" IsVisible="{Binding TodosQuery.IsSuccess}">
    <CollectionView.ItemTemplate>
        <DataTemplate><Label Text="{Binding Title}" /></DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### Query Options

Control staleness, polling, placeholder data, and retries:

```csharp
TaskQuery = client.UseQuery(
    queryKey: ["tasks", projectId, taskId],
    queryFn: async ctx => await api.GetTaskAsync(projectId, taskId, ctx.CancellationToken),
    enabled: taskId > 0,
    staleTime: TimeSpan.FromSeconds(15),
    placeholderData: (_, _) =>
    {
        // Seed from a parent list cache for instant perceived load
        var cached = client.GetQueryData<List<TaskItem>>(["tasks", projectId]);
        return cached?.FirstOrDefault(t => t.Id == taskId);
    }
);
```

### Query Keys

Keys are hierarchical `List<object>` with C# 12 collection expression syntax. Invalidating a prefix cascades to all queries underneath:

```csharp
public static class QueryKeys
{
    public static QueryKey Projects => ["projects"];
    public static QueryKey Project(int id) => ["projects", id];
    public static QueryKey Tasks(int projectId) => ["projects", projectId, "tasks"];
}

// Invalidates Projects, Project(3), and Tasks(3)
await client.InvalidateQueriesAsync(["projects"]);
```

### Reusable Query Definitions

`QueryOptions<TData>` bundles key + function + config into a single typed object (analogous to TanStack v5's `queryOptions()`). The same definition works with `UseQuery`, `FetchQueryAsync`, `GetQueryData`, and `SetQueryData`:

```csharp
public static class Queries
{
    public static QueryOptions<IEnumerable<Project>> Projects(IProjectApi api) => new()
    {
        QueryKey = QueryKeys.Projects,
        QueryFn = async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
        StaleTime = TimeSpan.FromSeconds(60),
    };
}

// Observe reactively
ProjectsQuery = client.UseQuery(Queries.Projects(api));

// Read cache (TData inferred)
var cached = client.GetQueryData(Queries.Projects(api));

// Prefetch in background
await client.PrefetchQueryAsync(Queries.Projects(api));
```

### Select Transform

Cache one type, expose another. The cache stores the full object; the observer transforms it:

```csharp
TodoCountQuery = client.UseQuery(
    queryKey: ["todos"],
    queryFn: async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    select: todos => todos.Count
);
// TodoCountQuery.Data is int, but the cache holds List<Todo>
```

## Mutations

`UseMutation` creates a `MutationViewModel` with `MutateCommand` (an `IAsyncRelayCommand` for XAML binding) and lifecycle callbacks:

```csharp
CreateTodoMutation = client.UseMutation<Todo, string>(
    mutationFn: async (title, context, ct) =>
        await api.CreateTodoAsync(title, ct),
    onSuccess: async (todo, title, context) =>
    {
        await context.Client.InvalidateQueriesAsync(["todos"]);
    }
);

// Fire from code
await CreateTodoMutation.MutateCommand.ExecuteAsync("Buy milk");
```

```xml
<!-- Or bind in XAML -->
<Button Text="Create" Command="{Binding CreateTodoMutation.MutateCommand}" CommandParameter="Buy milk" />
```

### Optimistic Updates

Update the cache before the server responds. Roll back on error:

```csharp
UpdateStatusMutation = client.UseMutation<TaskItem, TaskItemStatus>(
    mutationFn: async (status, context, ct) =>
        await api.UpdateStatusAsync(taskId, status, ct),
    options: new()
    {
        OnMutate = async (newStatus, context) =>
        {
            await context.Client.CancelQueriesAsync(["tasks", taskId]);

            var previous = context.Client.GetQueryData<TaskItem>(["tasks", taskId]);
            if (previous is not null)
            {
                context.Client.SetQueryData(["tasks", taskId],
                    previous with { Status = newStatus });
            }

            return null;
        },
        OnError = async (_, _, _, context) =>
        {
            await context.Client.InvalidateQueriesAsync(["tasks", taskId]);
        }
    }
);
```

## Infinite Queries

Cursor-based pagination with `UseInfiniteQuery`:

```csharp
CommentsQuery = client.UseInfiniteQuery(
    new InfiniteQueryObserverOptions<PagedResult<Comment>, string?>
    {
        QueryKey = ["tasks", taskId, "comments"],
        QueryFn = async ctx =>
            await api.GetCommentsAsync(taskId, ctx.PageParam, pageSize: 10, ctx.CancellationToken),
        InitialPageParam = null,
        GetNextPageParam = ctx => ctx.Page.NextCursor is { } cursor
            ? PageParamResult<string?>.Some(cursor)
            : PageParamResult<string?>.None,
    }
);
```

The `InfiniteQueryViewModel` exposes `HasNextPage`, `HasPreviousPage`, `FetchNextPageCommand`, and `FetchPreviousPageCommand` for binding.

## Cache Operations

```csharp
// Read cached data
var todos = client.GetQueryData<List<Todo>>(["todos"]);

// Write cached data
client.SetQueryData(["todos"], updatedTodos);

// Update with a function
client.SetQueryData<List<Todo>>(["todos"], prev => prev?.Append(newTodo).ToList());

// Invalidate (marks stale, triggers refetch for active observers)
await client.InvalidateQueriesAsync(["todos"]);

// Prefetch (silent failures, warms cache)
await client.PrefetchQueryAsync(new FetchQueryOptions<List<Todo>>
{
    QueryKey = ["todos"],
    QueryFn = async ctx => await api.GetTodosAsync(ctx.CancellationToken),
});

// Imperative fetch (throws on failure)
var result = await client.FetchQueryAsync(new FetchQueryOptions<List<Todo>>
{
    QueryKey = ["todos"],
    QueryFn = async ctx => await api.GetTodosAsync(ctx.CancellationToken),
});
```

## Focus and Network Refetching

Queries automatically refetch when the app regains focus or reconnects. Wire up the platform signals:

```csharp
// MAUI
protected override void OnResume()
{
    base.OnResume();
    FocusManager.Instance.SetFocused(true);
}

Connectivity.ConnectivityChanged += (s, e) =>
    OnlineManager.Instance.SetOnline(e.NetworkAccess == NetworkAccess.Internet);
```

## Architecture

For design decisions, reactive flow, and component responsibilities, see [`ARCHITECTURE.md`](ARCHITECTURE.md).

## License

MIT License - See LICENSE file for details

## Acknowledgments

- Inspired by [TanStack Query](https://tanstack.com/query) by Tanner Linsley
- Built with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM support
