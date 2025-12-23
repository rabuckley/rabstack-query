# Architecture Reference

This document contains detailed architecture documentation, API examples, extension patterns, and implementation details for RabStack Query. For concise project guidance, see [`CLAUDE.md`](CLAUDE.md).

## Core Reactive Flow

RabStack Query implements a **reactive observer pattern** with Redux-like state management:

```
QueryClient → QueryCache → Query<TData> → QueryObserver<TData, TQueryData> → QueryViewModel
    ↓              ↓            ↓                    ↓                              ↓
Orchestrator   Storage    State Machine      Subscription Layer              MVVM Wrapper
```

### Critical Design Patterns

**1. State Management via Reducer Pattern**

`Query<TData>` uses a reducer with `Dispatch(DispatchAction)` to manage state transitions:
- `FetchAction` - Sets FetchStatus to Fetching (or Paused when offline with NetworkMode.Online)
- `ErrorAction<TData>` - Sets status to Error, increments failure count
- `FailedAction<TData>` - Updates failure count during retries (stays Fetching)
- `InvalidateAction` - Sets IsInvalidated flag
- `PauseAction` - Sets FetchStatus to Paused (retryer entered pause due to offline/unfocused)
- `ContinueAction` - Sets FetchStatus to Fetching (retryer resumed from pause)
- `SetStateAction<TData>` - Replaces entire state

All state updates notify observers via `NotifyManager.Instance.Batch()`.

**2. Observer Subscription Chain**

When creating a `QueryObserver<TData, TQueryData>`:
1. Calls `QueryCache.Build()` to get/create `Query<TQueryData>`
2. Sets query function via `SetQueryFn()`
3. Adds itself to query's observer list
4. On first subscription, triggers fetch if `Enabled = true`
5. Receives updates via `OnQueryUpdate()` callback
6. Transforms data using `Select` function (TQueryData → TData)
7. Computes derived state (IsLoading = Status == Pending && IsFetching)

**3. Two-Level Type System**

`QueryObserver<TData, TQueryData>` has two type parameters:
- `TQueryData` - Type stored in cache (source data)
- `TData` - Type returned to consumer (transformed data via Select)

Example: Cache stores `List<Todo>`, observer returns `int` (count).

**3b. Options Type Hierarchy**

Four option types serve different purposes at different layers:

| Type | Purpose | Scope |
|------|---------|-------|
| `QueryConfiguration<TData>` | Cache-level config passed to `QueryCache.Build` | GcTime, Retry, InitialData, NetworkMode |
| `QueryOptions<TData>` | Reusable query definition (TanStack v5 `queryOptions()`) | Key + QueryFn + cache config (StaleTime, GcTime, NetworkMode) |
| `QueryObserverOptions<TData, TQueryData>` | Observer-level config for `QueryObserver` | Select, RefetchInterval, Enabled, PlaceholderData, etc. |
| `FetchQueryOptions<TData>` | Imperative fetch config for `FetchQueryAsync` | Key + QueryFn + StaleTime + Retry |

`QueryOptions<TData>` covers the 80% case — simple queries with no Select transform or observer-level options. For queries that need Select transforms, polling, or other observer-level config, use `QueryObserverOptions<TData, TQueryData>` directly.

`QueryObserverOptions` is a `record`, enabling `with` expressions for creating variants:

```csharp
// Store base options at construction time
var baseOptions = new QueryObserverOptions<int, IEnumerable<Project>>
{
    QueryKey = ["projects"],
    QueryFn = queryFn,
    Select = projects => projects.Sum(p => p.TaskCount),
    RefetchInterval = PollingInterval,
    StaleTime = TimeSpan.FromSeconds(5),
};

// Toggle polling with a single property change
observer.SetOptions(baseOptions with { RefetchInterval = TimeSpan.Zero });
```

**4. Deterministic Query Key Hashing**

`DefaultQueryKeyHasher` ensures consistent hashing:
- Serializes to JSON with sorted object properties
- Same data in different order = same hash
- Used for cache lookups: `QueryKey → QueryHash → Query<TData>`

**5. Garbage Collection Pattern**

`Removable` base class (inherited by Query/Mutation):
- Starts timer on creation
- Calls `OptionalRemove()` after GcTime (default: 5 min)
- Query checks: `if (_observers.Count == 0 && FetchStatus == Idle) _cache.Remove(this)`
- Prevents memory leaks from unused queries

**6. Query Filters and Partial Key Matching**

`QueryFilters` + `QueryKeyMatcher` enable all bulk operations to match queries by prefix key, type (active/inactive), staleness, fetch status, and arbitrary predicates:

```csharp
// Invalidate all queries whose key starts with ["todos"] and refetch active ones
await client.InvalidateQueries(new InvalidateQueryFilters { QueryKey = ["todos"] });

// Invalidate without refetching
await client.InvalidateQueries(new InvalidateQueryFilters { QueryKey = ["todos"], RefetchType = InvalidateRefetchType.None });

// Only active, stale queries
client.RefetchQueries(new QueryFilters { QueryKey = ["todos"], Type = QueryTypeFilter.Active, Stale = true });
```

`QueryKeyMatcher.PartialMatchKey` compares elements positionally using sorted-JSON serialization (same approach as `DefaultQueryKeyHasher`). A filter with fewer elements than the key acts as a prefix match.

**7. Three-Level Query Defaults Merging**

Options are resolved through: global defaults → per-key-prefix defaults → per-query options.

```csharp
// Global defaults (lowest precedence)
client.SetDefaultOptions(new QueryClientDefaultOptions { GcTime = TimeSpan.FromSeconds(60), Retry = 5 });

// Per-key-prefix defaults (middle precedence, matched via PartialMatchKey)
client.SetQueryDefaults(["todos"], new QueryDefaults { QueryKey = ["todos"], GcTime = TimeSpan.FromMilliseconds(10_000) });

// Per-query options (highest precedence) — set directly on QueryConfiguration<TData>
```

**8. Query Cancellation**

`Query<TData>` stores the active `Retryer<TData>` as a field and captures pre-fetch state. `Cancel(CancelOptions?)` cancels the retryer and optionally reverts state:

```csharp
// Cancel all todo queries, reverting to pre-fetch state
await client.CancelQueriesAsync(new QueryFilters { QueryKey = ["todos"] });

// Cancel without revert
await query.Cancel(new CancelOptions { Revert = false });
```

`OperationCanceledException` is handled separately from real errors in `Fetch()` — cancellations skip the `ErrorAction` dispatch, so the query status doesn't transition to Errored.

## Critical Implementation Details

### Manual Cache Updates (SetQueryData/GetQueryData)

When calling `SetQueryData<TData>()`:
- If query exists: Calls `internal SetState()` directly on `Query<TData>`
- If query doesn't exist: Creates query via `QueryCache.Build()` with initial state
- Always sets `Status = Success`, `FetchStatus = Idle`, `IsInvalidated = false`

`SetState()` is `internal` (not `private`) so `QueryClient` can call it directly without reflection. This is an intentional bypass of the reducer pattern for manual cache updates, and keeps the code trim-safe and AOT-compatible.

### Imperative Fetch Methods

Three methods on `QueryClient` for fetching without creating persistent observer subscriptions:

```csharp
// Fetch (errors propagate, retry defaults to 0)
var data = await client.FetchQueryAsync(new FetchQueryOptions<string>
{
    QueryKey = ["todos"],
    QueryFn = async ct => await api.GetTodos(ct),
    StaleTime = TimeSpan.FromSeconds(60)  // skip fetch if cache is fresh
});

// Prefetch (swallows all errors, warms cache silently)
await client.PrefetchQueryAsync(new FetchQueryOptions<string>
{
    QueryKey = ["todos"],
    QueryFn = async ct => await api.GetTodos(ct)
});

// Ensure (returns cached data if fresh, else fetches)
var data = await client.EnsureQueryDataAsync(new FetchQueryOptions<string>
{
    QueryKey = ["todos"],
    QueryFn = async ct => await api.GetTodos(ct),
    StaleTime = TimeSpan.FromSeconds(60)
});
```

### Bulk Query Data Operations

```csharp
// Get data from multiple queries
var results = client.GetQueriesData<string>(new QueryFilters { QueryKey = ["todos"] });

// Update data across multiple queries
client.SetQueriesData<string>(
    new QueryFilters { QueryKey = ["todos"] },
    old => old + "-updated");

// Remove queries from cache
client.RemoveQueries(new QueryFilters { QueryKey = ["stale"] });

// Reset queries to initial state (active ones refetch)
client.ResetQueries(new QueryFilters { QueryKey = ["todos"] });

// Count fetching queries
int fetching = client.IsFetching(new QueryFilters { QueryKey = ["todos"] });

// Count pending mutations
int mutating = client.IsMutating(new MutationFilters { MutationKey = ["todos"] });
```

### Mutation Filters

`MutationFilters` mirror `QueryFilters` for mutations, supporting key (prefix or exact), status, and predicate filters:

```csharp
var cache = client.GetMutationCache();

// Find mutations by key prefix
var todoMutations = cache.FindAll(new MutationFilters { MutationKey = ["todos"] });

// Find by status
var pending = cache.FindAll(new MutationFilters { Status = MutationStatus.Pending });

// First match
var mutation = cache.Find(new MutationFilters { MutationKey = ["todos"], Exact = true });
```

### Focus/Online Refetch Pattern

`QueryClient` accepts optional `IFocusManager`/`IOnlineManager` parameters, defaulting to the global singletons. This follows the same injection pattern as `TimeProvider`:

```csharp
// Production — uses global singletons (default)
var client = new QueryClient(new QueryCache());

// Tests — isolated instances prevent cross-test interference
var focusManager = new FocusManager();
var onlineManager = new OnlineManager();
var client = new QueryClient(new QueryCache(),
    focusManager: focusManager, onlineManager: onlineManager);
```

Platform code must call:
```csharp
FocusManager.Instance.SetFocused(bool);
OnlineManager.Instance.SetOnline(bool);
```

### Mutation Lifecycle and API

#### Type Parameters (4-Parameter System)

`Mutation<TData, TError, TVariables, TOnMutateResult>` uses four type parameters:

- **TData**: Type of data returned by mutation (e.g., `Todo`)
- **TError**: Error type, must inherit from `Exception` (defaults to `Exception`)
- **TVariables**: Type of input variables (e.g., `string` for title)
- **TOnMutateResult**: Type of context returned by `OnMutate` callback (defaults to `object?`)

**MVVM Layer Simplification**: `MutationViewModel<TData, TVariables>` uses `Exception` and `object?` as defaults for TError and TOnMutateResult, keeping the public API simple.

#### Context Pattern (OnMutate Return Value)

The `OnMutate` callback returns a context value that flows through all subsequent callbacks:

```csharp
options: new MutationOptions<Todo, Exception, string, IEnumerable<Todo>?>
{
    OnMutate = async (title, context) =>
    {
        // Capture previous state for rollback
        var previousTodos = context.Client.GetQueryData<IEnumerable<Todo>>(["todos"]);

        // Return context for use in OnError
        return previousTodos;
    },
    OnError = async (error, variables, previousTodos, context) =>
    {
        // Roll back using context from OnMutate
        if (previousTodos is not null)
        {
            context.Client.SetQueryData(["todos"], previousTodos);
        }
    }
}
```

#### MutationFunctionContext

Passed to mutation function and all callbacks:

```csharp
public sealed class MutationFunctionContext
{
    public QueryClient Client { get; }  // Access to cache operations
    public MutationMeta? Meta { get; }  // Arbitrary metadata dictionary
    public QueryKey? MutationKey { get; }  // Optional mutation identifier
}
```

Usage:
```csharp
MutationFn = async (title, context, ct) =>
{
    // context.Client provides access to QueryClient methods
    return await context.Client.FetchQueryAsync(...);
}
```

#### Callback Signatures

All callbacks receive the full context and function context:

- **OnMutate**: `Func<TVariables, MutationFunctionContext, Task<TOnMutateResult>>`
  - Returns context value for subsequent callbacks

- **OnSuccess**: `Func<TData, TVariables, TOnMutateResult?, MutationFunctionContext, Task>`
  - Parameters: (data, variables, context, functionContext)

- **OnError**: `Func<TError, TVariables, TOnMutateResult?, MutationFunctionContext, Task>`
  - Parameters: (error, variables, context, functionContext)

- **OnSettled**: `Func<TData?, TError?, TVariables, TOnMutateResult?, MutationFunctionContext, Task>`
  - Parameters: (data, error, variables, context, functionContext)

#### Per-Call Option Overrides

Override callbacks for individual mutation invocations using `MutateOptions`:

```csharp
await mutation.MutateAsync(variables, new MutateOptions<Todo, Exception, string, object?>
{
    OnSuccess = async (data, variables, context, functionContext) =>
    {
        // This overrides the default OnSuccess for this call only
        Console.WriteLine($"Custom success for {variables}");
    }
});
```

#### Lifecycle Order

```
Idle → Pending → cache.OnMutate → mutation.OnMutate → Execute → Success/Error
  → cache.OnSuccess/OnError → mutation.OnSuccess/OnError
  → cache.OnSettled → mutation.OnSettled
```

**Cache-level callbacks** (`MutationCacheConfig`) fire for every mutation that executes through the cache, enabling global error handling, logging, or toast notifications. They always run before the corresponding mutation-level callbacks and are fully awaited before the next callback starts. Pass the config when constructing a `MutationCache`:

```csharp
var cache = new MutationCache(new MutationCacheConfig
{
    OnError = async (error, variables, context, mutation, functionContext) =>
    {
        // Global error toast for all mutations
        ShowToast($"Mutation failed: {error.Message}");
    }
});
var client = new QueryClient(new QueryCache(), mutationCache: cache);
```

**Optimistic updates pattern:**
1. `OnMutate`: Capture previous state and update cache optimistically
2. Execute mutation (MutationFn runs)
3. `OnSuccess`: Confirm changes (e.g., `await InvalidateQueries()` for server refresh)
4. `OnError`: Rollback using context from OnMutate
5. `OnSettled`: Final cleanup (runs whether success or error)

#### Additional MutationOptions Properties

- **MutationKey**: `QueryKey?` - Optional identifier for mutation tracking
- **Meta**: `MutationMeta?` - Metadata dictionary for custom data
- **Scope**: `MutationScope?` - Future: mutation coordination and isolation
- **NetworkMode**: `NetworkMode` - Offline behavior (Online|Always|OfflineFirst). Implemented for queries; pending for mutations
- **Retry**: `int` - Number of retry attempts (default: 0)
- **RetryDelay**: `Func<int, Exception, TimeSpan>?` - Custom retry delay calculation
- **GcTime**: `TimeSpan` - Garbage collection time (default: 5 minutes)

## Timer and Scheduling — Per-Component Detail

`QueryClient` owns a `TimeProvider` instance (constructor parameter, defaults to `TimeProvider.System`) and threads it to every component:

- **`Removable`** (base class for `Query`/`Mutation`) accepts `TimeProvider` in its constructor. GC timers use `_timeProvider.CreateTimer(...)` (returning `ITimer`), passing the `TimeSpan`-typed `GcTime` directly. A `protected GetUtcNowMs()` helper calls `_timeProvider.GetUtcNow().ToUnixTimeMilliseconds()` for subclass use.
- **`Query<TData>`** uses `GetUtcNowMs()` for `DataUpdatedAt`, `ErrorUpdatedAt`, and initial data timestamps. Passes `TimeProvider` to `RetryerOptions`.
- **`QueryObserver`** uses `_client.TimeProvider.CreateTimer(...)` for refetch interval polling and `_client.TimeProvider.GetUtcNow()` for staleness checks.
- **`QueryResult`** accepts an optional `TimeProvider` for its `IsStale` property computation.
- **`Retryer`** uses a `DelayAsync` helper built on `TimeProvider.CreateTimer` (the BCL doesn't provide a `TimeProvider.Delay` extension). `DefaultRetryDelay` returns `TimeSpan`.
- **`Mutation`** uses `GetUtcNowMs()` for `SubmittedAt` and passes `TimeProvider` to `RetryerOptions`.

**Testing pattern:** Pass `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) to `QueryClient` for deterministic time control in tests. Call `fakeTimeProvider.Advance(TimeSpan)` to trigger GC timers, staleness transitions, retry backoff, and refetch intervals without wall-clock delays. See `TimeProviderTests.cs` for examples.

## Metrics — Instrument Catalogue and Recording Points

**Meter name:** `"RabstackQuery"`. All instrument names use the `rabstackquery.` prefix with dotted hierarchy.

| Category | Instruments |
|---|---|
| Query Fetch | `fetch_total`, `fetch_success_total`, `fetch_error_total`, `fetch_cancelled_total`, `fetch_deduplicated_total`, `fetch_duration` |
| Cache | `hit_total`, `miss_total`, `size`, `gc_removed_total` |
| Mutation | `total`, `success_total`, `error_total`, `duration` |
| Retry | `total` (with `source` tag: `"query"` or `"mutation"`) |
| Observer | `active` (UpDownCounter) |
| Invalidation | `invalidation_total`, `refetch_on_focus_total`, `refetch_on_reconnect_total` |

**Tags:** Query fetch metrics carry a `rabstackquery.query.hash` tag (bounded by distinct query keys in the app). Mutation metrics carry `rabstackquery.mutation.key` when `MutationKey` is set. Error types, query key contents, and observer IDs are intentionally omitted to avoid cardinality explosion.

**Duration measurement:** Fetch and mutation durations use `Stopwatch` (not `TimeProvider`) because metrics should reflect real wall-clock time, not test-controlled logical time. The stopwatch is only started when the histogram instrument is not null.

**Recording points:** `Query<TData>` records fetch/invalidation/refetch metrics. `QueryCache` records cache size. `QueryClient` records cache hit/miss. `Mutation` records mutation lifecycle. `Retryer` records retry attempts. `QueryObserver` records active observer count.

**Testing pattern:** Create an `IMeterFactory` from DI, pass it to `QueryClient`, then use `MetricCollector<T>` from `Microsoft.Extensions.Diagnostics.Testing` to assert on measurements. See `MetricsTests.cs`.

## Common Pitfalls — Code Examples

### 1. Forgetting to Dispose ViewModels
```csharp
// Wrong - leaks subscription
var viewModel = new QueryViewModel<Todo>(...);

// Correct
var viewModel = new QueryViewModel<Todo>(...);
// ... use viewModel ...
viewModel.Dispose();
```

### 2. Using Wrong Type Parameters in QueryObserver
```csharp
// Wrong - can't transform
var observer = new QueryObserver<int, int>(...);

// Correct - can transform List<Todo> to int
var observer = new QueryObserver<int, List<Todo>>(client, new QueryObserverOptions
{
    QueryFn = async ct => await api.GetTodos(ct),
    Select = todos => todos.Count // Transform function
});
```

### 3. Using Exact Key When Prefix Match is Intended
```csharp
// Only invalidates ["todos"] exactly — misses ["todos", 1], ["todos", 2], etc.
await client.InvalidateQueries(new InvalidateQueryFilters { QueryKey = ["todos"], Exact = true });

// Correct — prefix match invalidates the entire family
await client.InvalidateQueries(new InvalidateQueryFilters { QueryKey = ["todos"] });

// Convenience overload also does prefix match
await client.InvalidateQueries(["todos"]);
```

### 4. Query Hash Mismatch
Query keys with objects in different property order produce **same hash**:
```csharp
QueryKey key1 = ["todos", new { status = "done", page = 1 }];
QueryKey key2 = ["todos", new { page = 1, status = "done" }];
// key1 and key2 produce identical hash due to property sorting
```

### 5. Forgetting to Return Context from OnMutate
```csharp
// Wrong - doesn't return context for rollback
OnMutate = async (input, context) =>
{
    var previous = context.Client.GetQueryData<List<Todo>>(["todos"]);
    // Missing: return previous;
},

// Correct - returns context for use in OnError
OnMutate = async (input, context) =>
{
    var previous = context.Client.GetQueryData<List<Todo>>(["todos"]);
    return previous;  // Store for rollback
},
OnError = async (error, variables, previous, context) =>
{
    // Can now use 'previous' for rollback
    context.Client.SetQueryData(["todos"], previous);
}
```

### 6. Wrong Context Type in MutationOptions
```csharp
// Wrong - TOnMutateResult is object? but OnMutate returns List<Todo>
var options = new MutationOptions<Todo, Exception, string, object?>
{
    OnMutate = async (input, context) => context.Client.GetQueryData<List<Todo>>(["todos"])
};

// Correct - TOnMutateResult matches OnMutate return type
var options = new MutationOptions<Todo, Exception, string, List<Todo>?>
{
    OnMutate = async (input, context) => context.Client.GetQueryData<List<Todo>>(["todos"])
};
```

## Testing Patterns

### Creating Test QueryClient
```csharp
private static QueryClient CreateQueryClient()
{
    var queryCache = new QueryCache();
    return new QueryClient(queryCache);
}
```

### Testing with DefaultQueryKeyHasher
Use the shared singleton instance:
```csharp
var hash = DefaultQueryKeyHasher.Instance.HashQueryKey(["todos"]);
```

## File Organization

```
src/
├── RabstackQuery/              # Core library
│   ├── Query.cs                # State machine + observer list + Cancel/Reset
│   ├── QueryClient.cs          # Public API + cache orchestration + QueryCache + NotifyManager
│   ├── QueryObserver.cs        # Subscription + data transform
│   ├── QueryResult.cs          # IQueryResult implementation
│   ├── QueryFilters.cs         # QueryFilters + QueryTypeFilter for bulk operations
│   ├── QueryKeyMatcher.cs      # PartialMatchKey, ExactMatchKey, MatchQuery
│   ├── QueryClientDefaultOptions.cs # Global defaults for QueryClient
│   ├── FetchQueryOptions.cs    # Options for FetchQuery/PrefetchQuery/EnsureQueryData
│   ├── Mutation.cs             # Mutation state machine + MutationKey
│   ├── MutationCache.cs        # Mutation storage + FindAll/Find with MutationFilters
│   ├── MutationCacheConfig.cs  # Cache-level mutation callbacks (OnError, OnSuccess, OnMutate, OnSettled)
│   ├── MutationFilters.cs      # MutationFilters for key/status/predicate matching
│   ├── QueriesObserver.cs      # Multi-query observer group (parallel queries)
│   ├── MutationObserver.cs     # Mutation subscription
│   ├── CancelledError.cs       # CancelOptions for query cancellation
│   ├── Retryer.cs              # Exponential backoff logic
│   ├── QueryOptions.cs         # Reusable query definition (key + fn + config)
│   ├── QueryKey.cs             # Collection type
│   ├── DefaultQueryKeyHasher.cs # Deterministic hashing
│   ├── FocusManager.cs         # App focus singleton
│   ├── OnlineManager.cs        # Network connectivity singleton
│   ├── QueryMetrics.cs          # Centralised metrics instruments (18 instruments)
│   ├── RuntimeFeature.cs        # [FeatureSwitchDefinition] for trim-time metrics stripping
│   ├── QueryTimeDefaults.cs    # Shared default durations (GcTime = 5 min)
│   ├── RefetchOnBehavior.cs     # WhenStale/Never/Always enum for focus/reconnect
│   └── Removable.cs            # GC timer base class
│
└── RabstackQuery.Mvvm/         # MVVM bindings
    ├── QueryViewModel.cs       # Query wrapper with INotifyPropertyChanged
    ├── MutationViewModel.cs    # Mutation wrapper with IAsyncRelayCommand
    ├── QueryCollectionViewModel.cs  # ObservableCollection wrapper
    └── QueryClientExtensions.cs     # UseQuery/UseMutation extensions

test/
├── RabstackQuery.Tests/            # Core library tests
│   ├── QueryClientTests.cs         # Integration tests
│   ├── QueryClientFullTests.cs     # Extended client tests
│   ├── QueryTests.cs               # Query state machine tests
│   ├── QueryCacheTests.cs          # Cache storage tests
│   ├── QueryObserverTests.cs       # Observer subscription tests
│   ├── QueryObserverFullTests.cs   # Extended observer tests
│   ├── QueriesObserverTests.cs    # Multi-query observer group tests (12 tests)
│   ├── QueryKeyMatcherTests.cs     # Partial/exact key matching tests
│   ├── QueryFiltersTests.cs        # Filter-based bulk operation tests
│   ├── QueryDefaultsTests.cs       # Three-level defaults merging tests
│   ├── QueryCancellationTests.cs   # Cancel with revert, bulk cancel tests
│   ├── FetchQueryTests.cs          # FetchQuery/PrefetchQuery/EnsureQueryData tests
│   ├── MutationTests.cs            # Mutation lifecycle tests
│   ├── MutationCacheTests.cs       # Mutation cache tests
│   ├── MutationFiltersTests.cs     # Mutation filter matching tests
│   ├── DefaultQueryKeyHasherTests.cs  # Hash determinism tests
│   ├── CriticalBugFixTests.cs      # Regression tests for fixed bugs
│   ├── TimeProviderTests.cs        # Deterministic time tests with FakeTimeProvider
│   └── MetricsTests.cs             # Metrics instrument recording tests (18 tests)
│
└── RabstackQuery.Mvvm.Tests/       # MVVM layer tests
    ├── MutationViewModelTests.cs   # Command error swallowing, state propagation
    └── QueryViewModelTests.cs      # Refetch error swallowing, data updates

examples/
├── RabstackQuery.TrimAotValidation/ # Trim/AOT validation console app
│   └── Program.cs                   # Exercises fetch, mutate, invalidate, observer paths
```

## Extending the Library

### Adding New Action Types
1. Create new class inheriting `DispatchAction`
2. Add case to `Query<TData>.Dispatch()` reducer
3. Update state immutably (create new QueryState)
4. Notify observers via existing NotifyManager.Batch

### Adding New QueryObserver Features
1. Add property to `QueryObserverOptions<TData, TQueryData>`
2. Implement logic in `QueryObserver.UpdateResult()`
3. Ensure proper disposal in `Dispose()` method

### Adding New Bulk Operations
1. Add the method to `QueryClient` accepting `QueryFilters?`
2. Use `_queryCache.FindAll(filters)` to iterate matching queries
3. For mutations, use `_mutationCache.FindAll(MutationFilters?)`
4. Wrap mutations in `NotifyManager.Instance.Batch()` for single notification cycle

### Adding New Query/Mutation Properties to Filters
1. Add a new abstract member to `Query` base class (implement in `Query<TData>`)
2. Add a corresponding filter property to `QueryFilters` or `MutationFilters`
3. Add the AND check to `QueryKeyMatcher.MatchQuery` or `MutationCache.MatchMutation`

### Hydration / Dehydration

`QueryClient.Dehydrate()` serializes cache state; `QueryClient.Hydrate()` restores it.
Enables Blazor Server→WASM state transfer, local cache persistence, and
process-to-process state sharing.

#### Type Erasure and the Placeholder Model

JavaScript's `getQueryData` works regardless of type because there is no runtime
type checking. C# queries are `Query<TData>` — hydration doesn't know `TData` at
restore time. The solution uses three mechanisms:

1. **Dehydration**: `Query` base has `abstract Dehydrate()` → `Query<TData>` produces
   `DehydratedQuery` with `object?` data. No type information is needed.

2. **Hydrating new queries**: Created as `Query<object>` with
   `IsHydratedPlaceholder = true`. These are discoverable via `FindAll()`/`GetAll()`
   but `GetQueryData<T>()` returns `default` for them (type mismatch).

3. **Placeholder upgrade**: When `QueryCache.Build<TData>()` is called (observer
   subscription, `SetQueryData`), it detects the placeholder, removes it, and creates
   a properly-typed `Query<TData>` from the dehydrated state.

**`GetQueryData<T>` divergence from TanStack:** Before a placeholder is upgraded,
`GetQueryData<T>` returns `default` because the cache lookup finds `Query<object>`,
not `Query<T>`. TanStack's `getQueryData` always returns hydrated data because
JavaScript has no runtime type checking. Consumers who need pre-subscription access
to hydrated data can use `QueryCache.TryGetHydratedData<T>(hash)` (internal API).

**Upgrade atomicity:** The remove-then-add in `Build` is not atomic. Between
`Remove(placeholder)` and `Add(typedQuery)`, concurrent `FindAll`/`GetByHash`
calls won't see the query. This is acceptable because `Build<TData>` is
typically called on the UI thread (Blazor/MAUI).

#### Deliberate C# Divergences

- **No promise dehydration** — C# Tasks can't transfer across processes. Pending
  queries hydrate as state-only; observers trigger a fresh fetch.
- **No data transformers** — Skip `serializeData`/`deserializeData`. Consumers
  handle JSON serialization externally.
- **No error redaction** — Skip `shouldRedactErrors`.

### Creating Platform-Specific Managers
Follow FocusManager/OnlineManager pattern:
- Singleton with lazy initialization
- Platform-agnostic API (SetX methods)
- Event-based notification
- QueryClient subscribes in constructor
