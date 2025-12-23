using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace RabstackQuery;

internal sealed class DefaultQueryKeyHasher : IQueryKeyHasher
{
    /// <summary>
    /// Shared instance. The hasher is stateless, so there's no need to create one per call.
    /// </summary>
    public static readonly DefaultQueryKeyHasher Instance = new();

    // QueryKey is List<object> whose elements are primitives (strings, numbers,
    // booleans) and simple anonymous objects — all natively handled by
    // System.Text.Json without requiring unreferenced types or runtime codegen.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "QueryKey elements are JSON-serializable primitives and simple objects.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "QueryKey elements are JSON-serializable primitives and simple objects.")]
    public string HashQueryKey(QueryKey queryKey)
    {
        ArgumentNullException.ThrowIfNull(queryKey);

        var node = JsonSerializer.SerializeToNode(queryKey);
        var sorted = JsonNodeUtils.SortJsonNode(node);
        Debug.Assert(sorted is not null);
        return sorted.ToJsonString();
    }
}
