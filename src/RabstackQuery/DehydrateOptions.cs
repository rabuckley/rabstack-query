namespace RabstackQuery;

/// <summary>
/// Controls which queries and mutations are included in dehydration.
/// </summary>
public sealed class DehydrateOptions
{
    /// <summary>
    /// Custom filter for queries. Receives each query and returns whether it
    /// should be included in the dehydrated state. Default: only succeeded,
    /// non-placeholder queries.
    /// </summary>
    public Func<Query, bool>? ShouldDehydrateQuery { get; init; }

    /// <summary>
    /// Custom filter for mutations. Receives each mutation and returns whether
    /// it should be included. Default: only paused mutations.
    /// </summary>
    public Func<Mutation, bool>? ShouldDehydrateMutation { get; init; }

    /// <summary>
    /// Transforms query data during dehydration. Applied to each query's
    /// <see cref="DehydratedQueryState.Data"/> when not null. Use this to convert
    /// types that aren't directly serializable (e.g., <c>DateTime</c> to ISO 8601
    /// string). Pair with <see cref="HydrateOptions.DeserializeData"/> to reverse.
    /// </summary>
    /// <remarks>
    /// TanStack equivalent: <c>DehydrateOptions.serializeData</c>.
    /// Resolution: parameter -> client defaults -> null (no transform).
    /// </remarks>
    public Func<object?, object?>? SerializeData { get; init; }

    /// <summary>
    /// Determines whether a query's error should be redacted during dehydration.
    /// When this returns <see langword="true"/>, the query's
    /// <see cref="DehydratedQueryState.Error"/> and
    /// <see cref="DehydratedQueryState.FetchFailureReason"/> are replaced with a
    /// generic <see cref="Exception"/> with message "redacted". Default: always redact.
    /// </summary>
    /// <remarks>
    /// C# divergence: TanStack applies this to promise rejection errors (for SSR).
    /// Since C# Tasks can't be dehydrated, this is applied to state error fields
    /// instead, covering the use case of stripping sensitive details from serialized
    /// cache state.
    /// </remarks>
    public Func<Exception, bool>? ShouldRedactErrors { get; init; }
}
