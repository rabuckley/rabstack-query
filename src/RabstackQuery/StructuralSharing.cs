using System.Collections;

namespace RabstackQuery;

/// <summary>
/// Provides structural sharing utilities that preserve old object references
/// when new data is deeply equal. Analogous to TanStack's
/// <c>replaceEqualDeep</c> (<c>utils.ts:267</c>).
/// </summary>
public static class StructuralSharing
{
    private const int MaxDepth = 500;

    /// <summary>
    /// Returns <paramref name="prev"/> when deeply equal to
    /// <paramref name="next"/>, preserving reference identity. For
    /// collections, walks elements recursively; returns the previous
    /// collection reference when all elements are deeply equal.
    /// <para>
    /// Suitable as a <see cref="QueryObserverOptions{TData,TQueryData}.StructuralSharing"/>
    /// delegate: <c>StructuralSharing = StructuralSharing.ReplaceEqualDeep</c>.
    /// </para>
    /// </summary>
    public static TData ReplaceEqualDeep<TData>(TData prev, TData next)
    {
        var result = ReplaceEqualDeepCore(prev, next, 0);
        return (TData)result!;
    }

    private static object? ReplaceEqualDeepCore(object? prev, object? next, int depth)
    {
        if (ReferenceEquals(prev, next))
            return prev;

        if (depth > MaxDepth)
            return next;

        if (prev is null)
            return next;

        if (next is null)
            return next;

        // Both must be the same runtime type to compare structurally.
        // Different types → return next.
        if (prev.GetType() != next.GetType())
            return next;

        // Arrays: walk elements, return prev when all match.
        // Unlike TanStack's replaceEqualDeep which builds a partially-shared
        // array, we return prev only when ALL elements match. Partial
        // reconstruction would require Activator.CreateInstance, which is
        // incompatible with AOT/trimming.
        //
        // Element values are cached in locals to avoid re-boxing value types
        // on repeated indexer access — each access to IList[i] or Array.GetValue
        // boxes the value into a fresh object, breaking ReferenceEquals.
        if (prev is Array prevArray && next is Array nextArray)
        {
            if (prevArray.Length != nextArray.Length)
                return next;

            for (var i = 0; i < nextArray.Length; i++)
            {
                var prevElement = prevArray.GetValue(i);
                var merged = ReplaceEqualDeepCore(prevElement, nextArray.GetValue(i), depth + 1);
                if (!ReferenceEquals(merged, prevElement))
                    return next;
            }

            return prev;
        }

        // IList (List<T>, etc.): return prev when all elements deeply match
        if (prev is IList prevList && next is IList nextList)
        {
            if (prevList.Count != nextList.Count)
                return next;

            for (var i = 0; i < nextList.Count; i++)
            {
                var prevElement = prevList[i];
                var merged = ReplaceEqualDeepCore(prevElement, nextList[i], depth + 1);
                if (!ReferenceEquals(merged, prevElement))
                    return next;
            }

            return prev;
        }

        // IDictionary: return prev when all entries deeply match
        if (prev is IDictionary prevDict && next is IDictionary nextDict)
        {
            if (prevDict.Count != nextDict.Count)
                return next;

            foreach (DictionaryEntry entry in nextDict)
            {
                if (!prevDict.Contains(entry.Key))
                    return next;

                var prevValue = prevDict[entry.Key];
                var merged = ReplaceEqualDeepCore(prevValue, entry.Value, depth + 1);
                if (!ReferenceEquals(merged, prevValue))
                    return next;
            }

            return prev;
        }

        // Fallback: use Equals for records, IEquatable<T>, value types, etc.
        if (prev.Equals(next))
            return prev;

        return next;
    }
}
