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

        CreateTodo = client.UseMutation<Todo, CreateTodoRequest>(
            async (request, _, ct) => await api.CreateTodoAsync(request, ct),
            new MutationCallbacks<Todo, CreateTodoRequest>
            {
                OnSuccess = (_, _, _) => client.InvalidateQueries(["todos"])
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
| `MutateCancelCommand` | `ICommand` | Cancel a pending mutation |

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

- **UI Thread Marshaling**: All property changes are dispatched to `SynchronizationContext.Current`, captured at construction time
- **Disposal Required**: Always call `Dispose()` on ViewModels to unsubscribe from query observers and prevent memory leaks
- **Command Error Handling**: `RefetchCommand` and `MutateCommand` swallow exceptions by default for safe fire-and-forget binding. Use `InvokeAsync` when you need to handle errors

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
