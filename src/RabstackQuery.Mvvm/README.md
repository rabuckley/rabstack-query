# RabStack Query MVVM

MVVM bindings for [RabStack Query](https://www.nuget.org/packages/RabstackQuery) providing `INotifyPropertyChanged` ViewModels for MAUI, WPF, Avalonia, and other XAML frameworks. Built on [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) source generators.

## Installation

```bash
dotnet add package RabstackQuery.Mvvm
```

For Blazor apps, install [`RabstackQuery.Blazor`](https://www.nuget.org/packages/RabstackQuery.Blazor) instead, which includes this package and adds Blazor-specific component lifecycle management.

## Quick Start

```csharp
public partial class TodoListViewModel : ObservableObject, IDisposable
{
    public QueryViewModel<List<Todo>> TodosQuery { get; }
    public MutationViewModel<Todo, CreateTodoRequest> CreateTodo { get; }

    public TodoListViewModel(QueryClient client, ITodoApi api)
    {
        TodosQuery = client.UseQuery(
            ["todos"],
            async ctx => await api.GetTodosAsync(ctx.CancellationToken));

        CreateTodo = client.UseMutation(new MutationOptions<Todo, CreateTodoRequest>
        {
            MutationFn = async (request, _) => await api.CreateTodoAsync(request),
            OnSuccess = (_, _, _, context) => context.Client.InvalidateQueriesAsync(["todos"]),
        });
    }

    public void Dispose()
    {
        TodosQuery.Dispose();
        CreateTodo.Dispose();
    }
}
```

### XAML Binding

```xml
<StackLayout>
    <ActivityIndicator IsRunning="{Binding TodosQuery.IsLoading}"
                       IsVisible="{Binding TodosQuery.IsLoading}" />

    <Label Text="{Binding TodosQuery.Error.Message}"
           IsVisible="{Binding TodosQuery.IsError}" TextColor="Red" />

    <CollectionView ItemsSource="{Binding TodosQuery.Data}"
                    IsVisible="{Binding TodosQuery.IsSuccess}">
        <CollectionView.ItemTemplate>
            <DataTemplate>
                <Label Text="{Binding Title}" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>

    <Button Text="Refresh" Command="{Binding TodosQuery.RefetchCommand}" />
    <Button Text="Add Todo" Command="{Binding CreateTodo.MutateCommand}" />
</StackLayout>
```

## Creating ViewModels

Pass options to the `QueryClient` extension methods for callbacks and settings. Short forms support type inference:

| Method | Accepts |
|--------|---------|
| `UseQuery` | key + function; `QueryOptions<T>`; `QueryOptions<T>` + select; `QueryObserverOptions<T>`; `QueryObserverOptions<TData, TQueryData>` |
| `UseQueryCollection` | key + function + update; `QueryOptions<IEnumerable<T>>` + update; `QueryObserverOptions<IEnumerable<T>>` + update |
| `UseInfiniteQuery` | `InfiniteQueryObserverOptions<TData, TPageParam>` |
| `UseMutation` | mutation function; `MutationOptions<TData, TVariables>`; `MutationOptions<TData, TError, TVariables, TOnMutateResult>` |

Compose a reusable `QueryOptions<T>` with observer settings through `ToObserverOptions()` (or `ToObserverOptions(select)`) and a `with` expression:

```csharp
TaskQuery = client.UseQuery(
    Queries.Task(api, projectId, taskId).ToObserverOptions() with { Enabled = taskId > 0 });
```

`MutationOptions.Create` infers mutation types from a mutation lambda with typed parameters. Its two-delegate form also infers the optimistic rollback type from `onMutate`:

```csharp
StatusMutation = client.UseMutation(
    MutationOptions.Create(
        (TaskItemStatus status, MutationFunctionContext _) =>
            api.UpdateStatusAsync(id, status),
        onMutate: (status, _) =>
        {
            var previous = Status;
            Status = status;
            return Task.FromResult(previous);
        })
    with
    {
        OnError = (_, _, previous, _) => { Status = previous; return Task.CompletedTask; },
    });
```

`PageViewModel` (MAUI) and `RabstackComponentBase` (Blazor) expose the same `Use*` methods and dispose what they create.

Collection query functions must return `Task<IEnumerable<T>>`. An `async` lambda that awaits a `List<T>` adapts automatically; a method returning `Task<List<T>>` does not, because `Task<T>` is not covariant. Give reusable collection definitions an `IEnumerable<T>` data type.

### Migrating from flattened parameters

| Before | After |
|--------|-------|
| `UseQuery(key, fn, enabled: x, staleTime: y, ...)` | `UseQuery(QueryObserverOptions.Create(key, fn) with { Enabled = x, StaleTime = y })` |
| `UseQuery(key, fn, select: s, ...)` | `UseQuery(QueryOptions.Create(key, fn), s)` or `new QueryObserverOptions<TData, TQueryData> { ..., Select = s }` |
| `UseMutation<TData, TVariables>(fn, onSuccess: ...)` | `UseMutation(new MutationOptions<TData, TVariables> { MutationFn = fn, OnSuccess = ... })` |
| `UseMutation(fn, options)` | `UseMutation(options with { MutationFn = fn })` |
| `MutationDefinition.Create(fn)` | `MutationOptions.Create(fn)` |
| `OptimisticMutationDefinition.Create(fn, onMutate)` | `MutationOptions.Create(fn, onMutate)` |
| `MutationCallbacks` (three-argument callbacks) | `MutationOptions` callbacks, which also receive the `OnMutate` result |
| `new MutationViewModel<...>(client, fn, options)` | `new MutationViewModel<...>(client, options with { MutationFn = fn })` |
| `PageViewModel.Query`, `QueryCollection`, `InfiniteQuery`, `Mutation` | `UseQuery`, `UseQueryCollection`, `UseInfiniteQuery`, `UseMutation` |

Creating a mutation ViewModel without a `MutationFn` throws `ArgumentException` at construction.

## ViewModels

### QueryViewModel&lt;TData&gt;

Wraps a `QueryObserver` with observable properties for data binding:

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `TData?` | The last successfully resolved data |
| `Error` | `Exception?` | The error if the query is in an error state |
| `Status` | `QueryStatus` | `Pending`, `Error`, or `Success` |
| `FetchStatus` | `FetchStatus` | `Fetching`, `Paused`, or `Idle` |
| `IsLoading` | `bool` | First fetch in progress (no data yet) |
| `IsFetching` | `bool` | Any fetch in progress (including background) |
| `IsError` | `bool` | Query is in error state |
| `IsSuccess` | `bool` | Query has data |
| `IsStale` | `bool` | Cached data is past its stale time |
| `RefetchCommand` | `IAsyncRelayCommand` | Manually trigger a refetch |

### MutationViewModel&lt;TData, TVariables&gt;

Wraps a `MutationObserver` with observable properties and commands:

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `TData?` | The last mutation result |
| `Error` | `Exception?` | The error if the mutation failed |
| `Status` | `MutationStatus` | `Idle`, `Pending`, `Error`, or `Success` |
| `IsIdle` | `bool` | No mutation has been fired |
| `IsPending` | `bool` | Mutation is in progress |
| `IsError` | `bool` | Mutation failed |
| `IsSuccess` | `bool` | Mutation succeeded |
| `MutateCommand` | `IAsyncRelayCommand` | Fire the mutation (bindable) |

Call `InvokeAsync(variables)` for awaitable mutation execution with error propagation.

### QueryCollectionViewModel&lt;TItem&gt;

Wraps a query whose data is a collection, exposing an `ObservableCollection<TItem>` for efficient list binding:

```csharp
var todosCollection = client.UseQueryCollection(
    ["todos"],
    async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    (data, items) =>
    {
        items.Clear();
        if (data is not null)
            foreach (var todo in data) items.Add(todo);
    });

// Bind to todosCollection.Items in XAML
```

## Key Behaviors

- Construct ViewModels on the UI thread with a non-null `SynchronizationContext.Current`. They capture this context and post observer updates through it. Construction without a context throws `InvalidOperationException`.
- **Disposal Required**: Always call `Dispose()` on ViewModels to unsubscribe from query observers and prevent memory leaks
- **Command Error Handling**: `RefetchCommand` and `MutateCommand` swallow exceptions by default for safe fire-and-forget binding. Use `InvokeAsync` when you need to handle errors

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
