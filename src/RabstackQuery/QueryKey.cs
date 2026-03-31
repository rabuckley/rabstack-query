using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RabstackQuery;

/// <summary>
/// An ordered sequence of elements that uniquely identifies a query in the cache.
/// Supports C# collection expression syntax, e.g. <c>QueryKey key = ["todos", 1];</c>.
/// </summary>
[CollectionBuilder(typeof(QueryKeyCollectionBuilder), nameof(QueryKeyCollectionBuilder.Create))]
[DebuggerDisplay("{ToString(),nq}")]
public sealed class QueryKey : IEnumerable<object?>
{
    private readonly List<object> _list;

    private QueryKey(List<object> list)
    {
        _list = list;
    }

    /// <summary>
    /// Creates a <see cref="QueryKey"/> from the provided elements.
    /// </summary>
    /// <remarks>
    /// This factory is provided for languages that lack C# collection expression
    /// support (e.g. F#, VB.NET). C# callers should prefer the collection
    /// expression syntax: <c>QueryKey key = ["todos", 1];</c>
    /// </remarks>
    public static QueryKey Create(params object[] elements)
    {
        return new(new List<object>(elements));
    }

    /// <summary>
    /// Creates a <see cref="QueryKey"/> over the provided <paramref name="list"/>.
    /// </summary>
    /// <remarks>
    /// The caller must give up ownership of the list after calling this method.
    /// </remarks>
    /// <param name="list"></param>
    internal static QueryKey CreateUnsafe(List<object> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new(list);
    }

    public IEnumerator<object?> GetEnumerator() => _list.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns a human-readable bracket notation of the key elements,
    /// e.g. <c>["todos", 1]</c>. Strings are quoted, nulls shown as <c>null</c>.
    /// Also used as the default debugger display.
    /// </summary>
    public override string ToString() =>
        $"[{string.Join(", ", _list.Select(FormatElement))}]";

    private static string FormatElement(object? element) => element switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => element.ToString() ?? "null"
    };
}
