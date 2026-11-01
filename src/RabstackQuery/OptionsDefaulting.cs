namespace RabstackQuery;

internal static class OptionsDefaulting
{
    public static readonly TimeSpan FallbackStaleTime = TimeSpan.Zero;
    public static readonly TimeSpan FallbackGcTime = QueryTimeDefaults.GcTime;
    public const int FallbackQueryRetry = 3;
    public const int FallbackMutationRetry = 0;
    public const NetworkMode FallbackNetworkMode = NetworkMode.Online;
    public const RefetchOnBehavior FallbackRefetchOn = RefetchOnBehavior.WhenStale;

    private static readonly QueryDefaults EmptyQueryDefaults = new();
    private static readonly MutationDefaults EmptyMutationDefaults = new();

    public static QueryDefaults MergeQueryLayers(
        QueryDefaults? clientDefaults,
        IEnumerable<(QueryKey Key, QueryDefaults Defaults)> keyDefaults,
        QueryKey queryKey,
        QueryDefaults? perQuery)
    {
        var merged = clientDefaults ?? EmptyQueryDefaults;

        foreach (var (key, defaults) in keyDefaults)
        {
            if (QueryKeyMatcher.PartialMatchKey(queryKey, key))
            {
                merged = Overlay(merged, defaults);
            }
        }

        return perQuery is null ? merged : Overlay(merged, perQuery);
    }

    public static MutationDefaults MergeMutationLayers(
        MutationDefaults? clientDefaults,
        IEnumerable<(QueryKey Key, MutationDefaults Defaults)> keyDefaults,
        QueryKey? mutationKey,
        MutationDefaults? perMutation)
    {
        var merged = clientDefaults ?? EmptyMutationDefaults;

        if (mutationKey is not null)
        {
            foreach (var (key, defaults) in keyDefaults)
            {
                if (QueryKeyMatcher.PartialMatchKey(mutationKey, key))
                {
                    merged = Overlay(merged, defaults);
                }
            }
        }

        return perMutation is null ? merged : Overlay(merged, perMutation);
    }

    public static QueryDefaults Overlay(QueryDefaults lower, QueryDefaults upper) => new()
    {
        StaleTime = upper.StaleTime ?? lower.StaleTime,
        GcTime = upper.GcTime ?? lower.GcTime,
        Retry = upper.Retry ?? lower.Retry,
        RetryDelay = upper.RetryDelay ?? lower.RetryDelay,
        RefetchOnWindowFocus = upper.RefetchOnWindowFocus ?? lower.RefetchOnWindowFocus,
        RefetchOnReconnect = upper.RefetchOnReconnect ?? lower.RefetchOnReconnect,
        RefetchOnMount = upper.RefetchOnMount ?? lower.RefetchOnMount,
        NetworkMode = upper.NetworkMode ?? lower.NetworkMode,
    };

    public static MutationDefaults Overlay(MutationDefaults lower, MutationDefaults upper) => new()
    {
        Retry = upper.Retry ?? lower.Retry,
        RetryDelay = upper.RetryDelay ?? lower.RetryDelay,
        GcTime = upper.GcTime ?? lower.GcTime,
        NetworkMode = upper.NetworkMode ?? lower.NetworkMode,
    };
}

internal sealed record ResolvedQuerySettings
{
    public required TimeSpan StaleTime { get; init; }
    public required TimeSpan GcTime { get; init; }
    public required int Retry { get; init; }
    public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }
    public required RefetchOnBehavior RefetchOnWindowFocus { get; init; }
    public required RefetchOnBehavior RefetchOnReconnect { get; init; }
    public required RefetchOnBehavior RefetchOnMount { get; init; }
    public required NetworkMode NetworkMode { get; init; }

    public void CopyTo<TData>(QueryConfiguration<TData> destination)
    {
        destination.GcTime = GcTime;
        destination.Retry = Retry;
        destination.RetryDelay = RetryDelay;
        destination.NetworkMode = NetworkMode;
    }

    public static ResolvedQuerySettings From(QueryDefaults merged)
    {
        var networkMode = merged.NetworkMode ?? OptionsDefaulting.FallbackNetworkMode;

        return new ResolvedQuerySettings
        {
            StaleTime = merged.StaleTime ?? OptionsDefaulting.FallbackStaleTime,
            GcTime = merged.GcTime ?? OptionsDefaulting.FallbackGcTime,
            Retry = merged.Retry ?? OptionsDefaulting.FallbackQueryRetry,
            RetryDelay = merged.RetryDelay,
            RefetchOnWindowFocus = merged.RefetchOnWindowFocus ?? OptionsDefaulting.FallbackRefetchOn,
            RefetchOnReconnect = merged.RefetchOnReconnect
                ?? (networkMode is NetworkMode.Always ? RefetchOnBehavior.Never : RefetchOnBehavior.WhenStale),
            RefetchOnMount = merged.RefetchOnMount ?? OptionsDefaulting.FallbackRefetchOn,
            NetworkMode = networkMode,
        };
    }
}
