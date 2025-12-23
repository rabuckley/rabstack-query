# Remaining Work

This document tracks unimplemented features and test coverage gaps against
TanStack Query v5's `query-core`. Features already implemented (query filters,
defaults merging, cancellation, imperative fetch, mutation filters, batch
operations, query reset) are omitted.

## High Priority: Missing Features

### Hydration / Dehydration — Deferred Items

Core hydration (`Dehydrate`/`Hydrate` on `QueryClient`) is implemented. The
following TanStack feature is intentionally deferred:

- **Promise dehydration** — C# Tasks can't transfer across processes. Pending
  queries hydrate as state-only; observers trigger a fresh fetch.

**TanStack source:**
- `hydration.ts:23–81` — `DehydrateOptions`, `DehydratedState`
- `__tests__/hydration.test.tsx` — 33 tests (~11 skipped due to deferred items)

## Low Priority

### Mount/Unmount Lifecycle

TanStack uses `mount()`/`unmount()` with reference counting; our `IDisposable`
covers unmount but not mount ref-counting.

**TanStack source:**
- `queryClient.ts:80–98` — `mount()` / `unmount()`

### `QueryPersister`

Optional function on query options that acts as a custom data persistence
layer, intercepting the query function call. When set, `networkMode` defaults
to `offlineFirst`. This is distinct from hydration/dehydration — the persister
hooks into individual query fetches rather than serializing the whole cache.

**TanStack source:**
- `types.ts:122–136` — `QueryPersister` type definition
- `types.ts:249` — optional field on `QueryConfiguration`
- `query.ts:466` — called as fallback data source
- `queryClient.ts:612` — networkMode default override
- `infiniteQueryBehavior.ts:112–114` — infinite query persister path

## Remaining TanStack Tests to Port

- **QueryClient** (`queryClient.test.tsx`) — ~23 tests exercise unimplemented features
  (persister, skipToken, mount/unmount).
- **QueryObserver** (`queryObserver.test.tsx`) — additional observer option tests remain.
- **Query** (`query.test.tsx`) — retry with `retryOnMount`.
