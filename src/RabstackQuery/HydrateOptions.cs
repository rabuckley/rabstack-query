namespace RabstackQuery;

/// <summary>
/// Controls how dehydrated state is applied during hydration.
/// </summary>
public sealed class HydrateOptions
{
    /// <summary>Default options applied to all hydrated queries.</summary>
    public HydrateQueryDefaults? Queries { get; init; }

    /// <summary>Default options applied to all hydrated mutations.</summary>
    public HydrateMutationDefaults? Mutations { get; init; }

    /// <summary>
    /// Transforms query data during hydration. Applied to each query's
    /// <see cref="DehydratedQueryState.Data"/> when not null. Use this to reverse
    /// transforms applied by <see cref="DehydrateOptions.SerializeData"/>.
    /// </summary>
    /// <remarks>
    /// TanStack equivalent: <c>HydrateOptions.defaultOptions.deserializeData</c>.
    /// Resolution: parameter -> client defaults -> null (no transform).
    /// </remarks>
    public Func<object?, object?>? DeserializeData { get; init; }
}
