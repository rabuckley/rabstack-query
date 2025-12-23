namespace RabstackQuery.DevTools;

/// <summary>
/// Configuration for the RabStack Query DevTools overlay.
/// </summary>
public sealed class DevToolsOptions
{
    /// <summary>
    /// Formats query/mutation data for display in the detail view. The default
    /// uses <c>ToString()</c>. For AOT-safe JSON display, provide a formatter
    /// backed by a source-generated <c>JsonSerializerContext</c>:
    /// <code>
    /// DataFormatter = data => JsonSerializer.Serialize(data, AppJsonContext.Default.Options)
    /// </code>
    /// </summary>
    public Func<object?, string>? DataFormatter { get; init; }
}
