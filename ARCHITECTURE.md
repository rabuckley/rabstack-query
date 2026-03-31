# Architecture Reference

Design decisions and rationale that aren't obvious from reading the source code. For project guidance, see [`CLAUDE.md`](CLAUDE.md).

## Core Reactive Flow

```
QueryClient → QueryCache → Query<TData> → QueryObserver<TData, TQueryData> → QueryViewModel
    ↓              ↓            ↓                    ↓                              ↓
Orchestrator   Storage    State Machine      Subscription Layer              MVVM Wrapper
```

## Options Type Hierarchy Rationale

TanStack Query uses a single mega-object for all options. C# splits this into four types because each serves a different layer with different lifetime and composition needs:

| Type | Why it exists |
|------|---------------|
| `QueryConfiguration<TData>` | Cache-level config (`GcTime`, `Retry`, `InitialData`, `NetworkMode`) passed to `QueryCache.Build`. Separating this from observer options lets multiple observers share cache config without re-specifying it. |
| `QueryOptions<TData>` | Reusable query definition (key + fn + config). Analogous to TanStack v5's `queryOptions()` helper. Covers the 80% case where no `Select` transform or observer-level options are needed. |
| `QueryObserverOptions<TData, TQueryData>` | Observer-level config (`Select`, `RefetchInterval`, `Enabled`, `PlaceholderData`). A `record` so `with` expressions can toggle individual properties (e.g., switching polling on/off). |
| `FetchQueryOptions<TData>` | Imperative fetch config for `FetchQueryAsync`/`PrefetchQueryAsync`/`EnsureQueryDataAsync`. Exists because these methods don't create persistent observer subscriptions and need different defaults (e.g., `Retry = 0`). |

## Hydration / Dehydration

### The Problem

JavaScript's `getQueryData` works regardless of type because there's no runtime type checking. C# queries are `Query<TData>` — hydration doesn't know `TData` at restore time.

### The Placeholder Model

1. **Dehydration**: `Query` base has `abstract Dehydrate()` producing `DehydratedQuery` with `object?` data. No type information needed.

2. **Hydrating new queries**: Created as `Query<object>` with `IsHydratedPlaceholder = true`. Discoverable via `FindAll()`/`GetAll()` but `GetQueryData<T>()` returns `default` (type mismatch).

3. **Placeholder upgrade**: When `QueryCache.Build<TData>()` is called (observer subscription, `SetQueryData`), it detects the placeholder, removes it, and creates a properly-typed `Query<TData>` from the dehydrated state.

### Deliberate Divergences from TanStack

- **`GetQueryData<T>` returns `default` for unupgraded placeholders** — TanStack's `getQueryData` always returns hydrated data because JavaScript has no runtime type checking. Consumers needing pre-subscription access can use `QueryCache.TryGetHydratedData<T>(hash)` (internal API).

- **Upgrade atomicity** — The remove-then-add in `Build` is not atomic. Between `Remove(placeholder)` and `Add(typedQuery)`, concurrent `FindAll`/`GetByHash` calls won't see the query. Acceptable because `Build<TData>` is typically called on the UI thread (Blazor/MAUI).

- **No promise dehydration** — C# Tasks can't transfer across processes. Pending queries hydrate as state-only; observers trigger a fresh fetch.

- **No data transformers** — Skip `serializeData`/`deserializeData`. Consumers handle JSON serialization externally.

- **No error redaction** — Skip `shouldRedactErrors`.
