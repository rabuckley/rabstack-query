# RabStack Query

A powerful, type-safe data synchronization library for .NET, inspired by [TanStack Query](https://tanstack.com/query) (React Query). RabStack Query provides declarative query and mutation management with automatic caching, background refetching, and optimistic updates for MAUI, Blazor, and other .NET applications.

## Features

### Core Features
- **Declarative Data Fetching** - Define queries with simple functions, automatic execution and caching
- **Automatic Caching** - Intelligent caching with configurable stale time and cache time
- **Background Refetching** - Automatic refetch on window focus, network reconnection, and custom intervals
- **Retry Logic** - Exponential backoff retry with configurable attempts (1s, 2s, 4s, 8s, max 30s)
- **Stale-While-Revalidate** - Serve cached data immediately while refetching in background
- **Optimistic Updates** - Update UI immediately before server confirmation with automatic rollback
- **Mutation Support** - Full lifecycle hooks (onMutate, onSuccess, onError, onSettled)
- **Query Invalidation** - Granular cache invalidation and refetch triggering
- **Type Safety** - Full generic type support with nullable reference types

### MVVM Features
- **QueryViewModel<TData>** - INotifyPropertyChanged wrapper for reactive UI bindings
- **MutationViewModel<TData, TVariables>** - IAsyncRelayCommand support with cancellation
- **QueryCollectionViewModel<TItem>** - ObservableCollection wrapper for list queries
- **UI Thread Marshaling** - Automatic SynchronizationContext handling for cross-thread updates
- **CommunityToolkit.Mvvm Integration** - Source generators for minimal boilerplate

## Architecture

### Core Design Principles

RabStack Query follows a **reactive observer pattern** inspired by functional programming and Redux-like state management:

```
┌─────────────┐
│ QueryClient │ - Entry point, orchestrates cache and observers
└──────┬──────┘
       │
       ├─────► QueryCache ────┐
       │                      │
       │                      ▼
       │               ┌──────────┐
       │               │  Query   │ - State machine (Pending → Fetching → Success/Error)
       │               └────┬─────┘
       │                    │
       │                    ▼
       │          ┌──────────────────┐
       └─────────►│ QueryObserver    │ - Subscribes to query, transforms data
                  └──────────────────┘
                           │
                           ▼
                  ┌──────────────────┐
                  │  QueryViewModel  │ - MVVM wrapper with INotifyPropertyChanged
                  └──────────────────┘
                           │
                           ▼
                        UI Layer (MAUI, Blazor)
```

### Key Architectural Components

#### 1. QueryClient
The central orchestrator that manages caches and provides the primary API surface.

**Responsibilities:**
- Maintains QueryCache and MutationCache instances
- Provides high-level query/mutation methods
- Handles query invalidation and cache updates
- Subscribes to FocusManager and OnlineManager for automatic refetching

**Key Methods:**
- `QueryAsync<T>()` - Execute a one-time query
- `InvalidateQueries()` - Mark queries as stale
- `RefetchQueries()` - Force refetch
- `SetQueryData<T>()` / `GetQueryData<T>()` - Manual cache manipulation
- `MutateAsync<TData, TVariables>()` - Execute mutations

#### 2. Query<TData>
Represents a single cached query with state management via reducer pattern.

**State Machine:**
```
Idle → Pending → Fetching ⟷ (Retry) → Success/Error
                    ↓
              (invalidate)
                    ↓
                  Stale
```

**Key Properties:**
- `State` - Current query state (data, error, status, fetch status)
- `QueryKey` - Unique identifier for cache lookup
- `QueryHash` - Deterministic hash of query key
- `Options` - Configuration (retry, stale time, cache time)

**Key Features:**
- **Garbage Collection**: Automatically removes unused queries after `GcTime` (default: 5 minutes)
- **Retry with Exponential Backoff**: Configurable retry attempts with increasing delays
- **Observer Pattern**: Notifies all subscribed QueryObservers on state changes

#### 3. QueryObserver<TData, TQueryData>
Subscribes to query state changes and provides reactive updates to consumers.

**Responsibilities:**
- Create or reuse queries from QueryCache
- Transform query data via `Select` function
- Compute derived state (IsLoading, IsStale, etc.)
- Marshal updates to UI thread
- Trigger initial fetch when first subscriber attaches

**Type Parameters:**
- `TData` - Type returned to observer (after select transform)
- `TQueryData` - Type stored in cache (before select transform)

**Example:**
```csharp
// Cache stores List<Todo>, but observer returns only count
var observer = new QueryObserver<int, List<Todo>>(client, new QueryObserverOptions
{
    QueryKey = ["todos"],
    QueryFn = async ct => await api.GetTodos(ct),
    Select = todos => todos.Count // Transform: List<Todo> → int
});
```

#### 4. QueryCache
Thread-safe storage for all queries using ConcurrentDictionary.

**Responsibilities:**
- Store queries by hash for O(1) lookup
- Notify listeners on query add/remove/update
- Handle focus/online refetch triggers
- Batch notifications via NotifyManager

**Key Methods:**
- `Build<TData>()` - Get existing or create new query
- `Get<TData>()` - Retrieve query by hash
- `Remove()` - Delete query from cache
- `OnFocus()` / `OnOnline()` - Trigger background refetches

#### 5. QueryKey & DefaultQueryKeyHasher
**QueryKey** uses C# 12 collection expressions for concise syntax:
```csharp
QueryKey key = ["todos"];                          // Simple key
QueryKey key = ["todos", todoId];                  // With parameter
QueryKey key = ["todos", new { status, page }];    // With object
```

**DefaultQueryKeyHasher** provides deterministic hashing:
- Sorts object properties alphabetically for consistent hashing
- Handles nested objects, arrays, and null values
- Uses JSON serialization with custom converters
- Same objects in different order produce same hash

#### 6. Mutation<TData, TVariables>
Handles data modifications with lifecycle hooks.

**Lifecycle:**
```
Idle → Pending → (onMutate) → Execute → Success/Error
                                           ↓
                                    (onSuccess/onError)
                                           ↓
                                      (onSettled)
```

**Key Features:**
- **Optimistic Updates**: Update cache before API call via `onMutate`
- **Rollback**: Revert changes if mutation fails via `onError`
- **Invalidation**: Trigger refetch after success via `onSuccess`
- **Retry Support**: Optional retry with exponential backoff

#### 7. FocusManager & OnlineManager
Platform-agnostic singletons for lifecycle events.

**FocusManager:**
- Tracks whether app has window focus
- Triggers refetch when app regains focus
- Platform code calls `SetFocused(bool)`

**OnlineManager:**
- Tracks network connectivity status
- Triggers refetch when connection restored
- Platform code calls `SetOnline(bool)`

**Example (MAUI):**
```csharp
protected override void OnResume()
{
    base.OnResume();
    FocusManager.Instance.SetFocused(true);
}

// Network connectivity listener
Connectivity.ConnectivityChanged += (s, e) =>
{
    OnlineManager.Instance.SetOnline(e.NetworkAccess == NetworkAccess.Internet);
};
```

#### 8. Retryer<TData>
Handles retry logic with exponential backoff.

**Algorithm:**
```csharp
Attempt 1: Execute immediately
Attempt 2: Wait 1s  (2^0 * 1000ms)
Attempt 3: Wait 2s  (2^1 * 1000ms)
Attempt 4: Wait 4s  (2^2 * 1000ms)
Attempt 5: Wait 8s  (2^3 * 1000ms)
Max delay: 30s
```

**Cancellation:**
- Respects CancellationToken
- Stops retry loop immediately on cancel
- Propagates OperationCanceledException

#### 9. NotifyManager
Batches multiple state updates into single notification cycle.

**Purpose:**
- Prevent cascading updates during complex operations
- Batch multiple query invalidations into single render
- Defers notifications until transaction completes

**Usage:**
```csharp
NotifyManager.Instance.Batch(() =>
{
    query1.Invalidate();
    query2.Invalidate();
    query3.Invalidate();
    // All notifications fired together after batch completes
});
```

### MVVM Architecture

The MVVM package wraps core observers with `ObservableObject` from CommunityToolkit.Mvvm:

```
┌──────────────────────┐
│  QueryViewModel      │ - Wraps QueryObserver
│  [ObservableProperty]│ - Automatic INotifyPropertyChanged
└──────────┬───────────┘
           │
           ├─ Data: TData?
           ├─ IsLoading: bool
           ├─ IsError: bool
           ├─ Error: Exception?
           └─ RefetchCommand: IAsyncRelayCommand
```

**Key Features:**
- **Source Generators**: `[ObservableProperty]` eliminates boilerplate
- **Commands**: `[RelayCommand]` creates ICommand/IAsyncRelayCommand
- **Thread Safety**: Automatic SynchronizationContext marshaling
- **Disposable**: Proper cleanup of subscriptions

## Installation

```bash
# Core library
dotnet add package RabstackQuery

# MVVM bindings for MAUI/WPF/Avalonia
dotnet add package RabstackQuery.Mvvm
```

## Usage

### Basic Query

```csharp
// Create client (typically singleton)
var queryCache = new QueryCache();
var client = new QueryClient(queryCache);

// Execute query
var result = await client.QueryAsync(
    ["todos"],
    async ct => await api.GetTodos(ct)
);

// Access data
if (result.IsSuccess)
{
    var todos = result.Data;
}
```

### MVVM ViewModel (MAUI)

```csharp
public partial class TodosViewModel : ObservableObject
{
    private readonly QueryClient _client;
    private readonly ITodoApi _api;

    // Query with automatic UI updates
    public QueryViewModel<List<Todo>> TodosQuery { get; }

    // Mutation with optimistic updates
    public MutationViewModel<Todo, CreateTodoRequest> CreateTodoMutation { get; }

    public TodosViewModel(QueryClient client, ITodoApi api)
    {
        _client = client;
        _api = api;

        // Setup query
        TodosQuery = client.UseQuery(
            ["todos"],
            async ct => await api.GetTodos(ct)
        );

        // Setup mutation with lifecycle hooks
        CreateTodoMutation = client.UseMutation<Todo, CreateTodoRequest>(
            async (request, ct) => await api.CreateTodo(request, ct),
            new MutationOptions<Todo, CreateTodoRequest>
            {
                OnMutate = async (request) =>
                {
                    // Optimistic update
                    var todos = _client.GetQueryData<List<Todo>>(["todos"]) ?? new();
                    todos.Add(new Todo { Id = -1, Title = request.Title });
                    _client.SetQueryData(["todos"], todos);
                },
                OnSuccess = async (todo, request) =>
                {
                    // Invalidate to refetch with server data
                    _client.InvalidateQueries(["todos"]);
                },
                OnError = (error, request) =>
                {
                    // Rollback on error
                    _client.InvalidateQueries(["todos"]);
                }
            }
        );
    }
}
```

### XAML Binding (MAUI)

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             x:Class="MyApp.TodosPage">
    <StackLayout>
        <!-- Loading indicator -->
        <ActivityIndicator IsRunning="{Binding TodosQuery.IsLoading}"
                          IsVisible="{Binding TodosQuery.IsLoading}" />

        <!-- Error message -->
        <Label Text="{Binding TodosQuery.Error.Message}"
               IsVisible="{Binding TodosQuery.IsError}"
               TextColor="Red" />

        <!-- Todo list -->
        <CollectionView ItemsSource="{Binding TodosQuery.Data}"
                       IsVisible="{Binding TodosQuery.IsSuccess}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Label Text="{Binding Title}" />
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

        <!-- Add todo button -->
        <Button Text="Add Todo"
                Command="{Binding CreateTodoMutation.MutateCommand}"
                CommandParameter="{Binding NewTodoRequest}" />

        <!-- Refetch button -->
        <Button Text="Refresh"
                Command="{Binding TodosQuery.RefetchCommand}" />
    </StackLayout>
</ContentPage>
```

### Advanced: Query with Select Transform

```csharp
// Cache stores full user object, but component only needs name
var nameQuery = new QueryObserver<string, User>(client, new QueryObserverOptions
{
    QueryKey = ["user", userId],
    QueryFn = async ct => await api.GetUser(userId, ct),
    Select = user => user.FullName, // Transform User → string
    StaleTime = 5 * 60 * 1000, // 5 minutes
    CacheTime = 10 * 60 * 1000  // 10 minutes
});
```

### Advanced: Dependent Queries

```csharp
// First query
var userQuery = client.UseQuery(
    ["user", userId],
    async ct => await api.GetUser(userId, ct)
);

// Conditional second query based on first result
MutationViewModel<List<Post>, int>? postsQuery = null;
if (userQuery.IsSuccess && userQuery.Data is not null)
{
    postsQuery = client.UseQuery(
        ["posts", userQuery.Data.Id],
        async ct => await api.GetUserPosts(userQuery.Data.Id, ct)
    );
}
```

### Advanced: Pagination with QueryCollectionViewModel

```csharp
public partial class TodoListViewModel : ObservableObject
{
    public QueryCollectionViewModel<Todo> Todos { get; }

    [ObservableProperty]
    private int _page = 1;

    public TodoListViewModel(QueryClient client, ITodoApi api)
    {
        Todos = client.UseQueryCollection(
            ["todos", Page],
            async ct => await api.GetTodos(Page, ct)
        );
    }

    [RelayCommand]
    private async Task NextPage()
    {
        Page++;
        // Create new query for next page
        Todos = client.UseQueryCollection(
            ["todos", Page],
            async ct => await api.GetTodos(Page, ct)
        );
    }
}
```

```xml
<!-- XAML -->
<ListView ItemsSource="{Binding Todos.Items}">
    <!-- Items automatically update when query refetches -->
</ListView>
<Button Text="Next Page" Command="{Binding NextPageCommand}" />
```

## Configuration Options

### QueryObserverOptions

```csharp
new QueryObserverOptions<TData, TQueryData>
{
    QueryKey = ["key"],                    // Required: Cache key
    QueryFn = async ct => { },             // Required: Fetch function
    Enabled = true,                        // Auto-fetch on subscribe
    StaleTime = 0,                         // 0 = always stale (milliseconds)
    CacheTime = 5 * 60 * 1000,            // 5 minutes until GC
    Select = data => transform(data),      // Transform cached data
    Retry = 3,                             // Number of retry attempts
    RetryDelay = (attempt, error) => {     // Custom retry delay
        return attempt * 1000;
    }
}
```

### MutationOptions

```csharp
new MutationOptions<TData, TVariables>
{
    MutationFn = async (vars, ct) => { },  // Required: Mutation function
    OnMutate = async (vars) => {           // Before mutation (optimistic)
        // Update cache optimistically
    },
    OnSuccess = async (data, vars) => {    // After success
        // Invalidate related queries
        client.InvalidateQueries(["key"]);
    },
    OnError = (error, vars) => {           // After error
        // Rollback optimistic updates
    },
    OnSettled = async () => {              // Always runs after success/error
        // Cleanup
    },
    Retry = 0,                             // Mutations don't retry by default
    GcTime = 5 * 60 * 1000                // 5 minutes until GC
}
```

## Performance Considerations

### Memory Management
- **Garbage Collection**: Unused queries are automatically removed after `GcTime` (default: 5 minutes)
- **Weak References**: Consider using for large data sets (not yet implemented)
- **Disposal**: Always dispose ViewModels to unsubscribe from observers

### Thread Safety
- **QueryCache**: Uses ConcurrentDictionary for thread-safe access
- **QueryStore**: Thread-safe query storage
- **NotifyManager**: Batches updates to prevent race conditions
- **SynchronizationContext**: MVVM layer marshals all property changes to UI thread

### Optimization Tips
1. **Use StaleTime wisely**: Set higher values for data that doesn't change often
2. **Batch invalidations**: Use `NotifyManager.Batch()` for multiple invalidations
3. **Select transforms**: Use to reduce memory for derived data
4. **ObservableCollection**: Use QueryCollectionViewModel for list queries to minimize allocations

## Comparison to TanStack Query

### Similarities
- **Core Concepts**: Query keys, caching, stale-while-revalidate, optimistic updates
- **API Design**: Similar method names and patterns (useQuery → UseQuery, useMutation → UseMutation)
- **State Management**: Reactive observer pattern with automatic UI updates
- **Lifecycle Hooks**: onSuccess, onError, onMutate, onSettled callbacks

### Differences

| Feature | TanStack Query (React) | RabStack Query (.NET) |
|---------|----------------------|---------------------|
| Language | JavaScript/TypeScript | C# with generics |
| UI Framework | React hooks | MVVM (INotifyPropertyChanged) |
| Async Primitives | Promise | Task\<T\> |
| Cancellation | AbortSignal | CancellationToken |
| Reactivity | React state/hooks | ObservableObject + PropertyChanged |
| Key Syntax | `['todos', id]` | `["todos", id]` (collection expressions) |
| Type Safety | TypeScript inference | Full generic constraints |
| Platform | Web (React) | Cross-platform (.NET: MAUI, Blazor, WPF, Avalonia) |
| Commands | N/A (React callbacks) | ICommand/IAsyncRelayCommand (MVVM) |

### C#-Specific Enhancements
- **Nullable Reference Types**: Enforced nullability for safer code
- **Source Generators**: CommunityToolkit.Mvvm eliminates boilerplate
- **Pattern Matching**: Used extensively in reducers and state checks
- **Collection Expressions**: C# 12 syntax for concise query keys
- **Primary Constructors**: Modern C# syntax throughout

## Roadmap

### Completed ✅
- [x] Core query execution and caching
- [x] Retry with exponential backoff
- [x] Stale-while-revalidate
- [x] Focus/online refetching
- [x] Mutation support with lifecycle hooks
- [x] Query invalidation and manual updates
- [x] MVVM package for MAUI
- [x] QueryViewModel, MutationViewModel, QueryCollectionViewModel
- [x] Extension methods (UseQuery, UseMutation)

### Planned 🚧
- [ ] Infinite queries with pagination
- [ ] Hydration/dehydration for SSR
- [ ] Query filters (complex invalidation patterns)
- [ ] Blazor package (separate from MAUI)
- [ ] Network mode (online/always/offlineFirst)
- [ ] Structural sharing (deep equality checks)
- [ ] DevTools integration
- [ ] Suspense-like loading boundaries
- [ ] Prefetching and preloading
- [ ] Query cancellation on component unmount

## License

MIT License - See LICENSE file for details

## Contributing

Contributions welcome! Please read CONTRIBUTING.md for guidelines.

## Acknowledgments

- Inspired by [TanStack Query](https://tanstack.com/query) by Tanner Linsley
- Built with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM support
- Uses modern C# 12 and .NET 10 features

## Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/rabstack-query/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/rabstack-query/discussions)
- **Documentation**: [Wiki](https://github.com/yourusername/rabstack-query/wiki)
