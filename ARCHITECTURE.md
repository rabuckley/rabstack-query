# Architecture Reference

Design decisions and rationale that aren't obvious from reading the source code. For project guidance, see [`CLAUDE.md`](CLAUDE.md).

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
| `QueryConfiguration<TData>` | Cache-level config (`GcTime`, `Retry`, `InitialData`, `NetworkMode`) passed to `QueryCache.GetOrCreate`. Separating this from observer options lets multiple observers share cache config without re-specifying it. |
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

Cache-level settings (`GcTime`, `Retry`, `RetryDelay`, `NetworkMode`) apply when a query is created.
A later observer with different values does not change an existing query; TanStack's
`query.setOptions` on each fetch has no C# equivalent yet.

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

## Omitted property tracking

TanStack uses `trackResult` and `notifyOnChangeProps` to suppress React re-renders based
on result properties. The MVVM adapter uses per-property
`INotifyPropertyChanged` events, so it omits that tracking machinery. CommunityToolkit's
generated setters suppress events when values are equal.

`QueryObserver` notifies on every query state change. `QueriesObserver` memoizes combined
results and suppresses notifications when the combined output is the same reference.

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

Both test projects disable reflection-based JSON serialization to catch accidental use
before an AOT publish.
