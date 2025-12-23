# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository. For detailed architecture, API examples, and extension patterns, see [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Project Overview

RabStack Query is a .NET 10.0 port of TanStack Query, providing declarative query and mutation management with automatic caching, background refetching, and optimistic updates.

- **RabstackQuery** — Core library with query/mutation primitives
- **RabstackQuery.Mvvm** — MVVM bindings for MAUI/Blazor via `INotifyPropertyChanged`
- **RabstackQuery.Tests** / **RabstackQuery.Mvvm.Tests** — xUnit test suites
- **RabstackQuery.TrimAotValidation** — Console app for verifying trim/AOT compatibility

All projects tracked in `RabstackQuery.slnx`. Use `dotnet sln add --solution-folder <folder> <project>` when adding new projects. Target platforms: MAUI and Blazor.

## Source of Truth

The [TanStack Query](https://github.com/TanStack/query) TypeScript implementation and its tests define canonical behavior. When porting a feature, understand the TS behavior first, then evaluate whether the same approach works under C#'s threading model.

C#'s concurrency model (Tasks, async/await, `SynchronizationContext`, `CancellationToken`) requires deliberate divergences from TypeScript. These divergences must be:

- **Intentional** — never accidental or from misunderstanding the TS source.
- **Documented** — comment explaining what the TS implementation does and why the C# version differs.
- **Tested** — dedicated tests covering thread safety, cancellation, and reentrancy scenarios.

The file `remaining.md` tracks unimplemented features with links to relevant TanStack source locations.

### APIs Without a Direct TanStack Equivalent

Before adding a new public API, check whether TanStack has an equivalent. If it doesn't, that's a strong signal the API shouldn't exist. If a C# API genuinely has no TanStack counterpart (e.g., `IDisposable`, MVVM wrappers):

1. **Explicitly design the semantics** — find the closest TanStack API and reason about whether its behavior applies.
2. **Document the reasoning** in a code comment at the call site.
3. **Write tests for the intended semantics, not the observed behavior.** Every test for a non-trivial behavioral choice should reference a TanStack source location or an explicit design decision.

## C#-Specific Porting Concerns

These areas have no TS equivalent and are where porting agents most likely introduce bugs. See `ARCHITECTURE.md` for per-component detail and code examples.

### Query Key Identity and Equality

All key comparison goes through serialization — `DefaultQueryKeyHasher` serializes to JSON with sorted properties for deterministic hashing, and `QueryKeyMatcher` uses the same approach for partial/exact matching. Cache lookups are by hash string. **Never compare keys via `==` or `.Equals()` on `QueryKey` directly.**

### Disposal and Lifetime Management

Every feature that creates subscriptions, holds references, or starts timers must have a corresponding cleanup path. `Removable` (Query/Mutation base class) implements `IDisposable` for GC timers. `QueryClient` disposes `FocusManager`/`OnlineManager` subscriptions. MVVM ViewModels dispose observer subscriptions. `QueryObserver` uses `Destroy()` for refetch interval cleanup. The TS source won't remind you about cleanup — think about it on every new feature.

### Type System Translation

Don't replicate TS type patterns 1:1. Key decisions: `QueryKey` is `List<object>` with collection expression support. Queries use `Exception?` for errors; mutations use `TError : Exception`. The TS `QueryOptions` mega-object is split into purpose-specific C# types: `QueryConfiguration<TData>` (cache-level config with GcTime, Retry, InitialData, NetworkMode), `QueryOptions<TData>` (reusable query definition bundling key + queryFn + config, analogous to TanStack v5's `queryOptions()`), `QueryObserverOptions<TData, TQueryData>` (observer-level with Select, StaleTime, RefetchInterval), and `FetchQueryOptions<TData>` (imperative fetch). The MVVM layer defaults `TError` to `Exception` and `TOnMutateResult` to `object?`. Simplified aliases `QueryViewModel<TData>`, `MutationViewModel<TData, TVariables>`, and `MutationCallbacks<TData, TVariables>` reduce generic parameter boilerplate.

### Timer and Scheduling Abstraction

All time-dependent code routes through `System.TimeProvider`, owned by `QueryClient` and threaded to all components. Durations use `TimeSpan`; timestamps remain `long` (Unix ms). `QueryTimeDefaults` centralises shared defaults.

**Invariant:** `src/` has zero direct references to `DateTimeOffset.UtcNow`, `new Timer(...)`, or `Task.Delay(...)`. Use `FakeTimeProvider` in tests for deterministic time control.

### Metrics and Observability

18 instruments in `QueryMetrics` (internal, singleton per `QueryClient`) via `System.Diagnostics.Metrics`. Pass `IMeterFactory` to `QueryClient` to enable; `null` (default) skips all recording. Meter name: `"RabstackQuery"`, prefix: `rabstackquery.`. Duration uses `Stopwatch` (wall-clock, not `TimeProvider`). All creation gated behind `RuntimeFeature.IsMeterSupported` for trim-time stripping.

## Build & Test Commands

**Avoid building the MAUI sample app** unless specifically working on it — prefer targeting individual projects.

This project uses **Microsoft Testing Platform 2** (via `global.json`). Filter syntax uses `--filter-method`, `--filter-class`, and `--filter-namespace` (not the old `--filter` expression syntax).

```bash
# Build all projects (uses RabstackQuery.slnx)
dotnet build

# Build specific project (preferred — much faster than full solution)
dotnet build src/RabstackQuery/RabstackQuery.csproj
dotnet build src/RabstackQuery.Mvvm/RabstackQuery.Mvvm.csproj

# Run specific test method (fully qualified name, supports * wildcards at start/end)
dotnet test --filter-method "RabstackQuery.Tests.DefaultQueryKeyHasherTests.Hash_ShouldReturnSameHash_ForSameInput"

# Run all tests in a class
dotnet test --filter-class "RabstackQuery.Tests.QueryObserverFullTests"

# Run all tests in a namespace
dotnet test --filter-namespace "RabstackQuery.Tests"

# Run tests for a specific project
dotnet test --project test/RabstackQuery.Tests/RabstackQuery.Tests.csproj

# Show per-test pass/fail with durations (diagnose slow or hanging tests)
dotnet test --output Detailed

# Verify zero Trimming/AOT warnings
dotnet publish examples/RabstackQuery.TrimAotValidation/RabstackQuery.TrimAotValidation.csproj \
    -c Release -r osx-arm64 /p:PublishAot=true \
    /p:SuppressTrimAnalysisWarnings=false /warnaserror
```

### MVVM Integration

- `QueryViewModel`/`MutationViewModel` use CommunityToolkit.Mvvm source generators
- `[ObservableProperty]` auto-generates INotifyPropertyChanged; `[RelayCommand]` auto-generates ICommand
- Always dispose ViewModels to unsubscribe from observers

## Project Configuration

### Nullable Reference Types

All projects enforce `<Nullable>enable</Nullable>` with `<WarningsAsErrors>nullable</WarningsAsErrors>`. Prefer restructuring control flow or `is not null` guards; use `Debug.Assert` for internal code or `ArgumentNullException.ThrowIfNull` for public arguments. Avoid the `!` null-forgiving operator.

### Trimming and Native AOT

Both library projects declare `<IsTrimmable>true</IsTrimmable>` and `<IsAotCompatible>true</IsAotCompatible>`. Metrics stripping via `RuntimeFeature.IsMeterSupported` with `[FeatureSwitchDefinition]`. JSON serialization warnings in `DefaultQueryKeyHasher`/`QueryKeyMatcher` are suppressed (key elements are primitives). No reflection in `src/`. All new code must pass `dotnet build` with zero IL2026/IL3050/IL2075 warnings.

### C# 12+ Features Used

Collection expressions, *not* primary constructors, pattern matching, required properties.

## Common Pitfalls

See `ARCHITECTURE.md` for full before/after code examples of each.

1. **Forgetting to dispose ViewModels** — leaks subscriptions. Always call `Dispose()`.
2. **Wrong type parameters in `QueryObserver`** — `TQueryData` is the cache type, `TData` is the transformed output.
3. **Using `Exact = true` when prefix match is intended** — omit `Exact` to invalidate the entire key family.
4. **Query hash mismatch** — objects with different property order produce the **same** hash (by design, via property sorting).
5. **Forgetting to return context from `OnMutate`** — the return value flows to `OnError`/`OnSettled` for rollback.
6. **Wrong `TOnMutateResult` type in `MutationOptions`** — must match the actual return type from `OnMutate`.

## Testing Patterns

You MUST have all tests passing before finishing work. Assume your changes _always_ caused any issues you're seeing.

### Avoiding Flaky Async Tests

Tests that observe side effects of fire-and-forget async work must not rely on `Task.Delay` with hardcoded timeouts. Use a `TaskCompletionSource` or `SemaphoreSlim` signalled from the query function so the test awaits actual completion. If `Task.Delay` is the only option, keep it generous and document why.

