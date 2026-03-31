using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Emits <see cref="Activity"/> spans for devtools actions so they appear
/// in OpenTelemetry traces (e.g. the Aspire dashboard). Gated behind the
/// same <c>Meter.IsSupported</c> feature switch that MAUI uses to strip
/// observability code in Release builds.
/// </summary>
internal static class DevToolsTracing
{
    internal const string SourceName = "RabstackQuery.DevTools";

    [FeatureSwitchDefinition("System.Diagnostics.Metrics.Meter.IsSupported")]
    private static bool IsSupported { get; } =
        AppContext.TryGetSwitch("System.Diagnostics.Metrics.Meter.IsSupported", out var v) ? v : true;

    private static readonly ActivitySource? Source =
        IsSupported ? new ActivitySource(SourceName) : null;

    private const string ActionTag = "rabstackquery.devtools.action";
    private const string QueryHashTag = "rabstackquery.query.hash";
    private const string QueryKeyTag = "rabstackquery.query.key";

    internal static Activity? StartQueryAction(
        DevToolsActionType action,
        string? queryHash,
        string? queryKeyDisplay)
    {
        if (Source is null) return null;

        var activity = Source.StartActivity($"DevTools {action}", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(ActionTag, action.ToString());
        if (queryHash is not null) activity.SetTag(QueryHashTag, queryHash);
        if (queryKeyDisplay is not null) activity.SetTag(QueryKeyTag, queryKeyDisplay);

        return activity;
    }

    internal static Activity? StartGlobalAction(DevToolsActionType action)
    {
        if (Source is null) return null;

        var activity = Source.StartActivity($"DevTools {action}", ActivityKind.Internal);
        activity?.SetTag(ActionTag, action.ToString());
        return activity;
    }

    internal enum DevToolsActionType
    {
        Refetch,
        Invalidate,
        Reset,
        Remove,
        TriggerLoading,
        RestoreLoading,
        TriggerError,
        RestoreError,
        ClearQueryCache,
        ClearMutationCache,
        ToggleOnline,
    }
}
