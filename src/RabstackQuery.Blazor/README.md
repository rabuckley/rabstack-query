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
        _createTodo = UseMutation<Todo, CreateTodoRequest>(
            async (request, ctx, ct) => await Api.CreateTodoAsync(request, ct),
            new MutationCallbacks<Todo, CreateTodoRequest>
            {
                OnSuccess = (_, _, _) => Client.InvalidateQueries(["todos"])
            });
    }
}
```

## Features

- **`UseQuery`** creates a `QueryViewModel` that is automatically subscribed and disposed with the component
- **`UseMutation`** creates a `MutationViewModel` with the same lifecycle management
- **`UseQueryCollection`** for `ObservableCollection`-backed list queries
- **`UseInfiniteQuery`** for paginated/infinite scroll queries
- **Render Coalescing** batches multiple property change notifications into a single `StateHasChanged()` call per tick
- **Automatic Disposal** of all tracked ViewModels when the component is removed from the render tree
- **`Track` / `Observe`** for integrating pre-composed ViewModels that own their own queries

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

## Documentation

See the [GitHub repository](https://github.com/rabuckley/rabstack-query) for full documentation, architecture guide, and examples.

## License

MIT
