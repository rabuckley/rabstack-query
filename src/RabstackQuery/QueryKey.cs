using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RabstackQuery;

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
