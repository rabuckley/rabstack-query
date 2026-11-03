# RabStack Query Blazor

Blazor component integration for [RabStack Query](https://www.nuget.org/packages/RabstackQuery). Provides a `RabstackComponentBase` with `UseQuery` / `UseMutation` hooks, automatic render coalescing, and component-scoped disposal.

## Installation

```bash
dotnet add package RabstackQuery.Blazor
```

Register `QueryClient` in your DI container:

```csharp
builder.Services.AddSingleton(new QueryClient(new QueryCache()));
```

## Quick Start

```razor
@inherits RabstackComponentBase
@inject IWeatherApi Api

@if (_forecast.IsLoading)
{
    <p>Loading...</p>
}
else if (_forecast.IsError)
{
    <p class="text-danger">@_forecast.Error?.Message</p>
}
else
{
    <table>
        @foreach (var item in _forecast.Data!)
        {
            <tr>
                <td>@item.Date</td>
                <td>@item.Summary</td>
            </tr>
        }
    </table>
}

<button @onclick="() => _forecast.RefetchCommand.Execute(null)">Refresh</button>

@code {
    private QueryViewModel<List<WeatherForecast>> _forecast = null!;

    protected override void OnInitialized()
    {
        _forecast = UseQuery(["weather"],
            ctx => Api.GetForecastAsync(ctx.CancellationToken));
    }
}
```

## Mutations

```razor
@code {
    private MutationViewModel<Todo, CreateTodoRequest> _createTodo = null!;

    protected override void OnInitialized()
    {
        _createTodo = UseMutation(new MutationOptions<Todo, CreateTodoRequest>
        {
            MutationFn = async (request, ctx) => await Api.CreateTodoAsync(request),
            OnSuccess = (_, _, _, context) => context.Client.InvalidateQueriesAsync(["todos"]),
        });
    }
}
```

## Features

- **`UseQuery`** creates a `QueryViewModel` that is automatically subscribed and disposed with the component
- **`UseMutation`** creates a `MutationViewModel` with the same lifecycle management
- **`UseQueryCollection`** for `ObservableCollection`-backed list queries
- **`UseInfiniteQuery`** for paginated/infinite scroll queries
- The `Use*` methods accept the same options as the `QueryClient` extension methods; see the [MVVM API guide](../RabstackQuery.Mvvm/README.md#creating-viewmodels)
- **Render Coalescing** batches multiple property change notifications into a single `StateHasChanged()` call per tick
- **Automatic Disposal** of all tracked ViewModels when the component is removed from the render tree
- **`Track` / `Observe`** for integrating pre-composed ViewModels that own their own queries
- **`BeginScope`** to group ViewModels for disposal when parameter values change

### Pre-composed ViewModels

When your component uses a ViewModel class that internally manages multiple queries and mutations:

```razor
@code {
    private ProjectListViewModel _vm = null!;

    protected override void OnInitialized()
    {
        _vm = Track(new ProjectListViewModel(Client, Api));
        Observe(_vm.ProjectsQuery);
        Observe(_vm.CreateProjectMutation);
    }
}
```

### Parameter-dependent queries

Blazor reuses a component when only its parameters change, so `OnInitialized` does not run again. Each `Use*` or `Track` call keeps its ViewModel subscribed until the component is disposed.

Build parameter-dependent ViewModels in a `ViewModelScope` and replace the scope when the values change. `OnParametersSet` can run with unchanged values, so check them first:

```razor
@code {
    [Parameter] public int ProjectId { get; set; }

    private TaskBoardViewModel _vm = null!;
    private ViewModelScope? _scope;

    protected override void OnParametersSet()
    {
        if (_scope is not null && _vm.ProjectId == ProjectId) return;

        _scope?.Dispose();
        _scope = BeginScope();
        _vm = _scope.Track(new TaskBoardViewModel(Client, Api, ProjectId));
        _scope.Observe(_vm.TasksQuery);
    }
}
```

For a single query, use `_scope.Track(Client.UseQuery(...))`. To preserve the previous key's data for `PlaceholderData`, create the query once and call `QueryViewModel.SetOptions` from `OnParametersSet`.

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
