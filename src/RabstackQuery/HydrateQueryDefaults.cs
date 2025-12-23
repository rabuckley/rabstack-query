namespace RabstackQuery;

/// <summary>
/// Default options applied to queries during hydration.
/// </summary>
public sealed class HydrateQueryDefaults
{
    public TimeSpan? GcTime { get; init; }

    public int? Retry { get; init; }

    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }
}
