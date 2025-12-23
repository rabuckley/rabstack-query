# Future Work

Items tracked here are deferred design ideas — not bugs or missing features for the current release. Each is a potential addition that becomes worthwhile only when usage patterns demonstrate clear demand.

## `QueryEndpoint<TData>` — Typed Key Without Fetch Function

A sealed wrapper around `QueryKey` with a phantom `TData` type parameter. Unlike `QueryOptions<TData>` (which bundles key + fetch + config), an endpoint is just a typed address — it says "this cache slot stores TData" without prescribing how to fetch it.

```csharp
public sealed class QueryEndpoint<TData>
{
    public QueryKey Key { get; }
    public QueryEndpoint(QueryKey key) { Key = key; }
    public static implicit operator QueryKey(QueryEndpoint<TData> e) => e.Key;
}

// Usage: static, no API dependency
public static class Endpoints
{
    public static QueryEndpoint<IEnumerable<Project>> Projects => new(["projects"]);
    public static QueryEndpoint<Project> Project(int id) => new(["projects", id]);
}

// Type-safe cache access — TData inferred, no generic annotation
var data = client.GetQueryData(Endpoints.Projects);
client.SetQueryData(Endpoints.Projects, updatedProjects);
await client.InvalidateQueries(Endpoints.Projects);
```

**Why it complements QueryOptions:** `QueryOptions<TData>` requires an API dependency (the `QueryFn`), so it can't be a static constant. `QueryEndpoint<TData>` is dependency-free — it's just key + type. This enables:
- Static key definitions shared across the entire app (not just within a scope that has an API reference)
- Type-safe `GetQueryData` / `SetQueryData` / `InvalidateQueries` without constructing a full `QueryOptions`
- A single source of truth for key-to-type mapping that prevents cross-definition type mismatches at compile time

**Key design question:** Whether `QueryOptions<TData>` should accept a `QueryEndpoint<TData>` for its key (so the type flows from endpoint → options → observer), or whether they remain parallel paths. The former provides compile-time enforcement; the latter is simpler.

**Why deferred:** `QueryOptions<TData>` already covers most use cases. The endpoint pattern is most valuable for large codebases where keys are shared across many modules and the API dependency in `Queries.cs` becomes awkward. Revisit when usage patterns emerge.

## `QueryDefinition<TData>` Abstract Class

Bundles key, fetch function, and configuration in a single type — modeled on Distant's `QueryDefinition` protocol where the definition struct IS the cache key via `Hashable` conformance.

```csharp
public abstract class QueryDefinition<TData>
{
    public abstract QueryKey QueryKey { get; }
    public abstract Task<TData> FetchAsync(QueryFunctionContext context);
    public virtual TimeSpan StaleTime => TimeSpan.Zero;
    public virtual TimeSpan GcTime => QueryTimeDefaults.GcTime;
    public virtual int Retry => 3;
}
```

**Why deferred:** `QueryOptions<TData>` provides equivalent DX with less ceremony — a sealed class with init properties is simpler than requiring inheritance. The definition pattern adds value when users want polymorphism (e.g., a base `PaginatedQuery<T>` with shared retry/stale logic), but that's an advanced pattern.

**Key design question:** Configuration properties (StaleTime, GcTime, Retry) must NOT participate in cache identity — two instances with different StaleTime should map to the same cache slot. Hash only `QueryKey` (not the definition instance).

## Source Generator for QueryOptions

Auto-generate `QueryOptions<TData>` factory methods from annotated query function methods:

```csharp
[QueryEndpoint("todos")]
public Task<IReadOnlyList<Todo>> GetTodosAsync(CancellationToken ct) => ...;

// Generated:
public static QueryOptions<IReadOnlyList<Todo>> Todos(TodoApi api) => new() { ... };
```

**Why deferred:** Source generators add build-time complexity and are hard to debug. The manual `Queries.cs` static class is trivial to write. A generator only becomes worthwhile at scale (dozens of endpoints). Should be a separate NuGet package (e.g., `RabstackQuery.Generators`).

## `MutationEndpoint<TData, TVariables>` and `MutationDefinition<TData, TVariables>`

Typed mutation descriptors, analogous to query types. Typed mutation keys would mainly help with `MutationScope` coordination and `MutationCache` inspection — advanced scenarios.

**Why deferred:** Mutations rarely share keys across observers. The primary DX win (`MutationCallbacks<TData, TVariables>` + `MutationViewModel<TData, TVariables>`) is already shipped. Revisit after demand emerges.
