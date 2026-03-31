namespace RabstackQuery;

// ── Dehydrated state containers ─────────────────────────────────────────

/// <summary>
/// Serializable snapshot of a <see cref="QueryClient"/>'s cache state.
/// Produced by <see cref="QueryClient.Dehydrate"/> and consumed by
/// <see cref="QueryClient.Hydrate"/>. Enables Blazor Server→WASM state
/// transfer, local cache persistence, and process-to-process state sharing.
/// </summary>
public sealed class DehydratedState
{
    public required IReadOnlyList<DehydratedQuery> Queries { get; init; }

    public required IReadOnlyList<DehydratedMutation> Mutations { get; init; }
}

// ── Options ─────────────────────────────────────────────────────────────
