# AGENTS.md

This file provides guidance to coding agents working in this repository. For design decisions and rationale not obvious from source, see [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Project Overview

RabStack Query is a .NET 10.0 port of TanStack Query, providing declarative query and mutation management with automatic caching, background refetching, and optimistic updates.

- **RabstackQuery** — Core library with query/mutation primitives
- **RabstackQuery.Mvvm** — MVVM bindings for MAUI/Blazor via `INotifyPropertyChanged`
- **RabstackQuery.Blazor** — Blazor component base class with `UseQuery`/`UseMutation` hooks
- **RabstackQuery.DevTools** / **.DevTools.Blazor** / **.DevTools.Maui** — Cache inspection tooling and its UI hosts
- **RabstackQuery.Tests** / **RabstackQuery.Mvvm.Tests** / **RabstackQuery.Blazor.Tests** — xUnit test suites
- **RabstackQuery.TrimAotValidation** — Console app for verifying trim/AOT compatibility

All projects tracked in `RabstackQuery.slnx`. Target platforms: MAUI and Blazor.

## Source of Truth

The [TanStack Query](https://github.com/TanStack/query) TypeScript implementation and its tests, at the commit pinned in `eng/tanstack.ref`, define canonical behavior. When porting a feature, understand the TS behavior first, then evaluate whether the same approach works under C#'s threading model.

Read TanStack from `.upstream/`, which holds the `query-core` and `react-query` sources and tests at the pinned commit. Not GitHub `main` or memory, which are different versions. A SessionStart hook runs `eng/fetch-tanstack.sh` to create or update it; run the script yourself if `.upstream/` is missing.

C#'s concurrency model (Tasks, async/await, `SynchronizationContext`, `CancellationToken`) requires deliberate divergences from TypeScript. These divergences must be:

- **Intentional** — never accidental or from misunderstanding the TS source.
- **Documented** — comment explaining what the TS implementation does and why the C# version differs.
- **Tested** — dedicated tests covering thread safety, cancellation, and reentrancy scenarios.

### Citing TanStack

Cite upstream by file and symbol or test name, never by line number: `query.ts Query#fetch`, `retryer.ts createRetryer`, `query.test.tsx 'should be garbage collected when unsubscribed to'`. Quote test names exactly, with upstream's quote characters, so they can be searched. Line numbers shift with every upstream release, while a renamed symbol fails a search in `.upstream/` and shows the citation needs updating. For a line-level reference, use a permalink with a full commit hash (`blob/<sha>/...#L123`). CI runs `eng/check-tanstack-citations.sh`, which rejects `file.ts:N` citations. A C# test that ports an upstream test uses a `Ports:` marker instead (see "Tracking upstream tests").

### Bumping the pin

Bumping `eng/tanstack.ref` adopts upstream changes. The upstream diff shows what changed since the old pin:

```bash
git -C .upstream fetch --depth=1 --filter=blob:none https://github.com/TanStack/query.git <new-sha>
git -C .upstream diff <old-sha> <new-sha> -- packages/query-core/src packages/react-query/src
```

After bumping:

1. Run `eng/check-upstream-tests.sh --since <old-sha>` to list test keys added and removed.
2. Run `eng/check-upstream-tests.sh` and resolve every stale `Ports:` marker and ledger entry. Upstream often rewords tests, so read the new test before re-pointing a citation.
3. Search `.upstream/` for the symbols and free-form test names cited in `src/` and `test/`, which the check doesn't cover, and update any that are gone.
4. Run `eng/check-upstream-tests.sh --update` and commit the summary.

### Tracking upstream tests

Every upstream test at the pin is ported, omitted, todo or unreviewed. `eng/check-upstream-tests.sh` keeps that account, and `--list` prints the unreviewed tests. CI requires the account to be accurate, and the unreviewed count can stay above zero.

- **Ported:** a `// Ports: <file> > <describe path> > <name>` line directly above a `[Fact]` or `[Theory]`, for a C# test that asserts the same behaviour as the upstream test. Several C# tests can port one upstream test.
- **Omitted or todo:** an entry in `eng/upstream-tests.json` with a `status`, `key` and `reason`. Use `omitted` when the test can't apply here, and `todo` when it was reviewed and still needs a port or fix. A key ending in ` > *` covers a whole prefix.
- **Unreviewed:** everything else. These tests have no entry.

A similar name is not evidence of a port, so read both tests before adding a marker. If the closest C# test misses upstream's main assertion, record a `todo`. After any change to a marker or entry, run `eng/check-upstream-tests.sh --update` and commit the summary.

### APIs Without a Direct TanStack Equivalent

Before adding a new public API, check whether TanStack has an equivalent. If it doesn't, that's a strong signal the API shouldn't exist. If a C# API genuinely has no TanStack counterpart (e.g., `IDisposable`, MVVM wrappers):

1. **Explicitly design the semantics** — find the closest TanStack API and reason about whether its behavior applies.
2. **Document the reasoning** in a code comment at the call site.
3. **Write tests for the intended semantics, not the observed behavior.** Every test for a non-trivial behavioral choice should reference a TanStack symbol or test (see "Citing TanStack") or an explicit design decision.

### Auditing inherited TanStack APIs

For each TanStack API being ported, check whether it solves a problem in the C#/MVVM execution model. Omit machinery specific to React rendering. Document the omitted construct, why it does not apply, and any behavior retained. See `ARCHITECTURE.md`, "Omitted property tracking", for an example.

## C#-Specific Porting Concerns

These areas have no TS equivalent and are where porting agents most likely introduce bugs.

### Query Key Identity and Equality

Hashing and matching share `QueryKeyEncoder`, which writes JSON without reflection and sorts dictionary keys ordinally. Keep these paths consistent so invalidation reaches every key sharing a cache entry. Cache lookups use hash strings. Never compare `QueryKey` instances with `==` or `.Equals()`.

### Disposal and Lifetime Management

Every feature that creates subscriptions, holds references, or starts timers must have a corresponding cleanup path. `Removable` (Query/Mutation base class) implements `IDisposable` for GC timers. `QueryClient` disposes `FocusManager`/`OnlineManager` subscriptions. MVVM ViewModels dispose observer subscriptions. `QueryObserver` uses `Destroy()` for refetch interval cleanup. The TS source won't remind you about cleanup — think about it on every new feature.

### Type System Translation

Don't replicate TS type patterns 1:1. Key decisions: `QueryKey` is `List<object>` with collection expression support. Queries use `Exception?` for errors; mutations use `TError : Exception`. The TS `QueryOptions` mega-object is split into purpose-specific C# types — see `ARCHITECTURE.md` for rationale. The MVVM layer defaults `TError` to `Exception` and `TOnMutateResult` to `object?` with simplified aliases to reduce generic boilerplate.

### Timer and Scheduling Abstraction

All time-dependent code routes through `System.TimeProvider` (owned by `QueryClient`). Durations use `TimeSpan`; timestamps remain `long` (Unix ms).

The build rejects clocks and timers listed in `src/BannedSymbols.txt` with `RS0030` so tests can control time with `FakeTimeProvider`. Route violations through the client's `TimeProvider`.

### Metrics and Observability

`QueryMetrics` uses `System.Diagnostics.Metrics`, enabled by passing `IMeterFactory` to `QueryClient` (`null` default skips recording). Duration instruments use `Stopwatch` (wall-clock, not `TimeProvider`). All creation gated behind `RuntimeFeature.IsMeterSupported` for trim-time stripping.

## Build & Test Commands

**Avoid building the MAUI sample app** unless specifically working on it — prefer targeting individual projects.

This project uses **Microsoft Testing Platform 2** (via `global.json`). Filter syntax uses `--filter-method`, `--filter-class`, and `--filter-namespace` (not the old `--filter` expression syntax).

Before finishing work, run CI's `build-and-test` checks below. Add new libraries and test projects to `RabstackQuery.CI.slnf`, which covers every library and test suite except MAUI. For changes to `RabstackQuery.DevTools.Maui`, also run `dotnet build src/RabstackQuery.DevTools.Maui/RabstackQuery.DevTools.Maui.csproj`.

```bash
# Match CI: check citations and upstream test accounting, then build and test every non-MAUI library and test suite
eng/check-tanstack-citations.sh
eng/check-upstream-tests.sh
dotnet build RabstackQuery.CI.slnf
dotnet test --solution RabstackQuery.CI.slnf

# Build all projects, including MAUI (uses RabstackQuery.slnx)
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

## Project Configuration

### Nullable Reference Types

Nullable warnings are errors. Prefer `is not null` guards; use `Debug.Assert` for internal code or `ArgumentNullException.ThrowIfNull` for public arguments. Avoid the `!` null-forgiving operator.

### Trimming and Native AOT

Keep libraries under `src/` trim- and AOT-safe. Fix IL analyzer warnings about unsafe reflection rather than suppressing them. `src/Directory.Build.props` treats library warnings as errors except NuGet vulnerability advisories. Tests and examples don't inherit that policy. Verify the published core library with the AOT command above.

## Common Pitfalls

1. **Forgetting to dispose ViewModels** — leaks subscriptions. Always call `Dispose()`.
2. **Wrong type parameters in `QueryObserver`** — `TQueryData` is the cache type, `TData` is the transformed output.
3. **Using `Exact = true` when prefix match is intended** — omit `Exact` to invalidate the entire key family.
4. **Query hash mismatch** — objects with different property order produce the **same** hash (by design, via property sorting).
5. **Forgetting to return context from `OnMutate`** — the return value flows to `OnError`/`OnSettled` for rollback.
6. **Wrong `TOnMutateResult` type in `MutationOptions`** — must match the actual return type from `OnMutate`.

## Testing Patterns

You MUST have all tests passing before finishing work, as run by `dotnet test --solution RabstackQuery.CI.slnf`. Assume your changes _always_ caused any issues you're seeing.

### Avoiding Flaky Async Tests

Tests that observe side effects of fire-and-forget async work must not rely on `Task.Delay` with hardcoded timeouts. Use a `TaskCompletionSource` or `SemaphoreSlim` signalled from the query function so the test awaits actual completion. If `Task.Delay` is the only option, keep it generous and document why.

