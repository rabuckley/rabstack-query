using System.Diagnostics;
using System.Text.Json.Nodes;

namespace RabstackQuery;

/// <summary>
/// Shared JSON node utilities used by <see cref="DefaultQueryKeyHasher"/> and
/// <see cref="QueryKeyMatcher"/> for deterministic key serialization.
/// </summary>
internal static class JsonNodeUtils
{
    /// <summary>
    /// Deep-clones a <see cref="JsonNode"/> tree, sorting object properties alphabetically
    /// so that structurally equivalent objects produce identical JSON strings regardless of
    /// original property order.
    /// </summary>
    internal static JsonNode? SortJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    var sorted = new JsonObject();

                    foreach (var kvp in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        sorted[kvp.Key] = kvp.Value is null ? null : SortJsonNode(kvp.Value);
                    }

                    return sorted;
                }

            case JsonArray arr:
                {
                    var clonedArray = new JsonArray();

                    foreach (var item in arr)
                    {
                        clonedArray.Add(item is null ? null : SortJsonNode(item));
                    }

                    return clonedArray;
                }

            default:
                // JsonValue or primitive -> deep clone via JSON
                Debug.Assert(node is not null);
                return JsonNode.Parse(node.ToJsonString());
        }
    }
}
