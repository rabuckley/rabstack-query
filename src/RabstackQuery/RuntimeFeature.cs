using System.Diagnostics.CodeAnalysis;

namespace RabstackQuery;

/// <summary>
/// Compile-time feature switches for trimmer and Native AOT.
/// </summary>
/// <remarks>
/// When <c>System.Diagnostics.Metrics.Meter.IsSupported</c> is set to <c>false</c> at
/// trim time (e.g. via <c>&lt;RuntimeHostConfigurationOption&gt;</c> in the app's
/// <c>.csproj</c>), the trimmer substitutes <see cref="IsMeterSupported"/> with the
/// constant <c>false</c>. All code guarded by this property — including the entire
/// <see cref="QueryMetrics"/> constructor body and all instrument type references — is
/// then dead-code eliminated. This is the same switch MAUI uses via its
/// <c>MetricsSupport</c> MSBuild property.
/// </remarks>
internal static class RuntimeFeature
{
    [FeatureSwitchDefinition("System.Diagnostics.Metrics.Meter.IsSupported")]
    internal static bool IsMeterSupported { get; } =
        AppContext.TryGetSwitch(
            "System.Diagnostics.Metrics.Meter.IsSupported",
            out bool isSupported)
            ? isSupported
            : true; // Enabled by default at runtime.
}
