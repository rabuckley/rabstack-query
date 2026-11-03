# Architecture Reference

Design decisions and rationale that aren't obvious from reading the source code. For project guidance, see [`AGENTS.md`](AGENTS.md).

## Core Reactive Flow

```
QueryClient → QueryCache → Query<TData> → QueryObserver<TData, TQueryData> → QueryViewModel
    ↓              ↓            ↓                    ↓                              ↓
Orchestrator   Storage    State Machine      Subscription Layer              MVVM Wrapper
```

## Options Type Hierarchy Rationale

TanStack Query uses a single mega-object for all options. C# splits this into purpose-specific types because each serves a different layer with different lifetime and composition needs:

| Type | Why it exists |
|------|---------------|
| `QueryConfiguration<TData>` | Holds cache settings and initial data. Observers and imperative fetches supply resolved settings under [query settings ownership](#query-settings-ownership). |
| `QueryOptions<TData>` | Reusable query definition (key + fn + config). Use `ToObserverOptions()` or `ToObserverOptions(select)` with `with` to add observer settings. The select conversion drops `StructuralSharing`, which operates on the selected output type. |
| `QueryObserverOptions<TData, TQueryData>` | Observer-level config (`Select`, `RefetchInterval`, `Enabled`, `PlaceholderData`). A `record` so `with` expressions can toggle individual properties (e.g., switching polling on/off). |
| `FetchQueryOptions<TData>` | Imperative fetch config for `FetchQueryAsync`/`PrefetchQueryAsync`/`EnsureQueryDataAsync`. Exists because these methods don't create persistent observer subscriptions and fall back to different defaults (no retries). |
| `QueryDefaults` / `MutationDefaults` | The defaultable subset, shared by client-wide defaults (`QueryClientDefaultOptions.Queries`/`.Mutations`) and key-prefix defaults (`SetQueryDefaults`/`SetMutationDefaults`), as TanStack shares one shape between `defaultOptions.queries` and `setQueryDefaults`. |

## Factory overloads

Keep creation logic in `QueryClientExtensions`. Options expose all settings without duplicating them
in positional parameter lists. The shortcuts support C# type inference:

- `UseQuery(key, fn)` and `UseMutation(fn)` infer types from the delegate.
- `UseQuery(queryOptions, select)` calls `ToObserverOptions(select)`.
- Same-type `UseQueryCollection<TItem>` overloads infer the collection item type without typed `update`
  parameters. Two-type overloads pass raw cached items to `update`, which can preserve existing item ViewModels.

`MutationOptions.Create` infers types from delegates, including rollback state from `onMutate`.
A `MutationViewModel` requires `MutationFn` at construction. Core options allow it to be null because
hydration can supply it through `HydrateMutationDefaults`.

`PageViewModel` and `RabstackComponentBase` delegate to the extensions and own the returned ViewModels.
The Blazor adapter also subscribes to change notifications. `AdapterParityTests` checks overload parity.

The observer-options factories take `subscribed` separately because core observers do not control
adapter subscriptions. React declares it on `UseBaseQueryOptions` and reads it every render;
ViewModels take later changes through `Subscribed`. Page factories use the page's value.

Composite ViewModels capture constructor parameters. `ViewModelScope` lets components release them when those parameters change. TanStack's `useBaseQuery` updates one observer through `setOptions`.
Use `QueryViewModel.SetOptions` to switch keys while preserving the previous query's data for `PlaceholderData`.

## Option Defaulting

TanStack's `defaultQueryOptions` spreads `{ ...defaultOptions.queries, ...getQueryDefaults(queryKey), ...options }`,
so any property the caller leaves `undefined` inherits. C# expresses "unset" as `null`: the
defaultable options (`StaleTime`, `GcTime`, `Retry`, `RetryDelay`, `RefetchOnWindowFocus`,
`RefetchOnReconnect`, `RefetchOnMount`, `NetworkMode`) are nullable on every input type, and
every set value wins, including `TimeSpan.Zero` and `RefetchOnBehavior.WhenStale`. Never treat a
particular value as "unset"; that made explicit framework defaults impossible to set.

`QueryClient` owns the only query merge (`ResolveQuerySettings`, folding layers through the pure
`OptionsDefaulting` helper):

1. Fold client-wide defaults, then each partially matching key-prefix registration in registration
   order, then the per-query values, with `??`.
2. Apply framework fallbacks: `StaleTime` 0, `GcTime` 5 minutes, `Retry` 3, `NetworkMode.Online`,
   `RefetchOn*` `WhenStale`. TanStack applies these at the point of use; applying them once keeps
   readers free of null checks.
3. Apply TanStack's dependent defaults: an unset `RefetchOnReconnect` is `Never` when `NetworkMode`
   is `Always`; a `SkipToken` query function disables the query.

The result is the internal `ResolvedQuerySettings`, the analogue of `DefaultedQueryObserverOptions`.
`QueryObserver` recomputes it in its constructor and `SetOptions` and reads only from it. `Query`
receives already defaulted `QueryConfiguration` options from `QueryCache.GetOrCreate` and reads them
when each fetch starts. Imperative fetches merge the same way and then default an unset `Retry` to 0,
as `fetchQuery` does after `defaultQueryOptions`. Hydration passes the hydrate defaults as the
per-query layer. Mutations fold the same way in `DefaultMutationOptions` and fill the fallbacks
(`Retry` 0, `GcTime` 5 minutes, `NetworkMode.Online`) into the defaulted options.

### Query settings ownership

Query settings (`GcTime`, `Retry`, `RetryDelay`, `NetworkMode`, `Meta`) follow TanStack's
last-writer-wins `query.setOptions`:

- Observers apply settings at construction and on `SetOptions`.
- Observer, page and stale imperative fetches apply settings when they start. Joining an
  in-flight fetch leaves settings unchanged.
- Client invalidations, refetches and resets use the last applied settings. Focus and reconnect
  use the settings of the first observer that requests a refetch.
- `QueryCache.GetOrCreate` applies settings only when creating a query.

A stale imperative fetch with unset `Retry` applies its default of 0 to later client refetches,
even with observers mounted. An observer fetch or options update can replace it.
The GC timeout keeps the largest `GcTime` supplied, even when `Options.GcTime` decreases.

C# preserves the key and initial-data fields on options updates because `Reset()` recomputes
its target from `Options`; TanStack stores a separate `#initialState`. A volatile reference
publishes each configuration, and each fetch reads it once to avoid mixing concurrent updates.
Observers look up the cached query before fetching so a refetch after `RemoveQueries` repopulates
the cache.

## Hydration / Dehydration

### The Problem

JavaScript's `getQueryData` works regardless of type because there's no runtime type checking. C# queries are `Query<TData>` — hydration doesn't know `TData` at restore time.

### The Placeholder Model

1. **Dehydration**: `Query` base has `abstract Dehydrate()` producing `DehydratedQuery` with `object?` data. No type information needed.

2. **Hydrating new queries**: Created as `Query<object>` with `IsHydratedPlaceholder = true`. Discoverable via `FindAll()`/`GetAll()` but `GetQueryData<T>()` returns `default` (type mismatch).

3. **Placeholder upgrade**: `QueryCache.GetOrCreate<TData, TQueryData>()` replaces the placeholder with a `Query<TData>` initialized from its dehydrated state. Observer subscription and `SetQueryData` trigger this upgrade.

### Deliberate Divergences from TanStack

- **`GetQueryData<T>` returns `default` for unupgraded placeholders** — TanStack's `getQueryData` always returns hydrated data because JavaScript has no runtime type checking. Consumers needing pre-subscription access can use `QueryCache.TryGetHydratedData<T>(hash)` (internal API).

- **Upgrade atomicity.** `GetOrCreate` uses `ConcurrentDictionary.TryUpdate` so replacement leaves no gap in cache lookups. It destroys losing candidates to clear their GC timers and retries. Listeners receive `QueryRemoved(placeholder)` then `QueryAdded(typed)` in one batch. Avoid factory-based `AddOrUpdate`, which can discard queries without clearing their timers.

  Concurrent `Hydrate` can update a placeholder after `GetOrCreate` snapshots it but before the swap, losing the newer state.

- **No promise dehydration** — C# Tasks can't transfer across processes. Pending queries hydrate as state-only; observers trigger a fresh fetch.

- **No data transformers** — Skip `serializeData`/`deserializeData`. Consumers handle JSON serialization externally.

- **No error redaction** — Skip `shouldRedactErrors`.

## Listener exceptions

`Subscribable` catches an exception from each listener, so it can't fail the fetch or mutation that raised the notification. A listener that throws while a query succeeds leaves the query `Succeeded`. The exception is logged (event 4801) and rethrown as unhandled on a later tick.

TanStack's behaviour depends on how a listener subscribes:

| Listener throws | Operation outcome | Exception goes to | Later listeners |
| --- | --- | --- | --- |
| `query-core` raw `subscribe` | Fails. Throwing on the fetch-start dispatch also skips the query function and leaves the query `pending`/`fetching`. | The fetch or mutation promise, or the `setQueryData` caller | Skipped |
| `notifyManager.batchCalls` (React, Preact, Angular adapters) | Unaffected | Uncaught error on a later tick | Skipped for that flush, including `batchCalls` listeners on other observers and the cache. Raw subscribers still run. |
| This port | Unaffected | Unhandled exception on a later tick, and the log | Skipped on the same observer or cache for that notification. Other observers and the cache still run. |

This port follows `batchCalls`, except for how far the skip reaches. It has to catch before deferring because its notifications flush synchronously.

The skip stops at one instance because the port doesn't distinguish `batchCalls` listeners from raw ones. `QueriesObserver`'s inner subscriptions are raw upstream and keep its state current, so a flush-wide skip would leave its results stale. Batches also span threads here, so a flush-wide skip could drop an unrelated thread's notification.

The rethrow goes where an `async void` method's exception would: the current `SynchronizationContext`, otherwise the thread pool. **An unhandled exception on the thread pool terminates the process**, as an uncaught error does by default in Node. The outcome of one posted to a `SynchronizationContext` depends on the platform. In a Debug build of the MAUI sample, it terminates the app on the iOS simulator. On Mac Catalyst the app keeps running and only the log records the exception, so test this path on iOS as well. Android, Blazor and Release builds haven't been tested.

## MVVM ViewModel threading

Construct `QueryViewModel`, `InfiniteQueryViewModel`, and `MutationViewModel` on the UI
thread. They capture `SynchronizationContext.Current`, throw `InvalidOperationException`
if it is null, and post observer updates through it.

- **Subscribe-time notifications are delivered.** Observers can notify inside `Subscribe`
  (the mount fetch), before the constructor's synchronous `CurrentResult` read. A queuing
  dispatcher runs that post after construction. It carries the result already applied, so
  the equality-checked setters raise no `PropertyChanged`.
- **Disposal is safe from any thread.** `Dispose` sets a volatile flag before
  unsubscribing. No posted update starts applying after `Dispose` returns, though one
  already running on the UI thread may finish.

## Pausing MVVM ViewModels

`Subscribed` ports the `subscribed` option from TanStack's `useBaseQuery`, which its React Native guide
pairs with screen focus. A hidden page keeps its ViewModel state but holds no subscriptions, so nothing in
the client references a page that is later popped. MAUI raises `Unloaded` at different times on each platform
and has no signal that a page has left for good, so `Dispose` cannot be tied to page lifetime.

`PageViewModel.Subscribed` controls all ViewModels created by its factories. Queries created while
unsubscribed show cached data without starting a mount fetch.

Page factories omit `subscribed` because a per-child value would be overwritten on the next hide and
show, potentially fetching a query the caller wanted detached. TanStack's `useQueries` also has one
subscription control for the group. To control a ViewModel independently of the page, create it with
`Client.UseQuery(options, subscribed: false)` and dispose it yourself.

`MutationViewModel` also has `Subscribed`, which TanStack's `useMutation` does not. React's unmount releases
that observer, but a subscribed observer here would keep its finished mutation in `MutationCache`.

A queued update from before unsubscribing is discarded on resume, because resuming has already applied a
newer result.

While unsubscribed, `QueryViewModel.SetOptions` shows the state of the query its new options select, because
TanStack's `useBaseQuery` reads `getOptimisticResult` on every render and ViewModels have no render pass.

## Omitted property tracking

TanStack uses `trackResult` and `notifyOnChangeProps` to suppress React re-renders based
on result properties. The MVVM adapter uses per-property
`INotifyPropertyChanged` events, so it omits that tracking machinery. CommunityToolkit's
generated setters suppress events when values are equal.

`QueryObserver` notifies on every query state change. `QueriesObserver` memoizes combined
results and suppresses notifications when the combined output is the same reference.

## Omitted mutation cancellation

TanStack's `mutationFn` takes `(variables, context)` and its mutations have no cancel. This port
matches that: `MutationFn` receives no token and no mutation API accepts one. Stopping the client
request would not undo a write the server may already have applied, so neither an error nor an
idle state would be accurate. A mutation function's own `OperationCanceledException`, such as an
`HttpClient` timeout, is an ordinary failure.

## Omitted caller cancellation tokens

TanStack's `refetch`, `fetchQuery`, `invalidateQueries` and page fetches take no abort signal, so
these APIs take no `CancellationToken`. A fetch belongs to the query and is shared by its observers,
so one caller's token would cancel it for all of them, while a caller joining an in-flight fetch
would have its token ignored. `QueryClient.CancelQueriesAsync` cancels a fetch. A caller that only
wants to stop waiting can use `Task.WaitAsync(token)`, which leaves the fetch running.
`QueryFunctionContext.CancellationToken` stays because it is TanStack's `signal`.

## Query key encoding

`QueryKeyEncoder` uses `Utf8JsonWriter` to avoid reflection in trimmed and Native AOT apps.
It accepts scalars, sequences, string-keyed `IDictionary` values with ordinally sorted keys,
and `IQueryKeyElement` values. Unsupported types throw `ArgumentException`.

- Use a dictionary or implement `IQueryKeyElement` instead of passing anonymous objects.
- Keep encoding independent of application JSON settings. Naming policies, ignored
  properties and converters must not change key identity.
- Hashing and matching share the encoder so filters reach keys with the same hash.
  For example, `1` and `1L` share a cache entry and match the same filters.
- Enums share key identity with their underlying numbers; `Guid` values share it with
  their string forms.
- Positive `DateTimeOffset` offsets encode `+` literally rather than as `\u002B`,
  changing hashes produced by the previous serializer.
- Object elements match as a whole. A dictionary containing only `status` does not match
  one containing both `status` and `page`.

Core, MVVM and Blazor tests disable JSON reflection to catch accidental use before
AOT publishing.
