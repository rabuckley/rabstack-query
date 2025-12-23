namespace RabstackQuery;

/// <summary>
/// Scope for mutation isolation and coordination. Mutations sharing the same
/// <see cref="Id"/> run sequentially — the second mutation waits for the first
/// to complete before it starts. Mutations in different scopes (or unscoped
/// mutations) run concurrently.
/// </summary>
public sealed class MutationScope
{
    /// <summary>
    /// Unique identifier for this scope.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Creates a new MutationScope with the specified identifier.
    /// </summary>
    public MutationScope(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }
}
