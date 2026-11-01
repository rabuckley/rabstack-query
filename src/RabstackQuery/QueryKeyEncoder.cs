using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RabstackQuery;

// A fixed set of key shapes avoids reflection-based serialization in trimmed and AOT apps.
internal static class QueryKeyEncoder
{
    private const int MaxDepth = 64;

    public static string Encode(QueryKey queryKey)
    {
        ArgumentNullException.ThrowIfNull(queryKey);
        return EncodeValue(queryKey);
    }

    /// <summary>
    /// Encodes each element of <paramref name="queryKey"/> separately. Each result is the exact
    /// text that element contributes to <see cref="Encode"/>, so two keys hash equally exactly
    /// when their element encodings are pairwise equal.
    /// </summary>
    public static string[] EncodeElements(QueryKey queryKey)
    {
        ArgumentNullException.ThrowIfNull(queryKey);

        var encoded = new List<string>();
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        foreach (var element in queryKey)
        {
            // Depth 1 matches the element's position inside the array Encode writes, so both
            // paths reject the same over-deep keys.
            Write(writer, element, depth: 1);
            writer.Flush();
            encoded.Add(Encoding.UTF8.GetString(buffer.WrittenSpan));
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
        }

        return [.. encoded];
    }

    private static string EncodeValue(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, value, depth: 0);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(Utf8JsonWriter writer, object? value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException(
                $"Query key exceeds the maximum nesting depth of {MaxDepth}. The key may contain a cycle.");
        }

        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;

            case IQueryKeyElement element:
                Write(writer, element.ToQueryKeyValue(), depth + 1);
                return;

            case string s:
                writer.WriteStringValue(s);
                return;

            case char c:
                writer.WriteStringValue(c.ToString());
                return;

            case bool b:
                writer.WriteBooleanValue(b);
                return;

            case Enum e:
                WriteEnum(writer, e);
                return;

            case int i: writer.WriteNumberValue(i); return;
            case long l: writer.WriteNumberValue(l); return;
            case double d: writer.WriteNumberValue(d); return;
            case float f: writer.WriteNumberValue(f); return;
            case decimal m: writer.WriteNumberValue(m); return;
            case short sh: writer.WriteNumberValue(sh); return;
            case byte by: writer.WriteNumberValue(by); return;
            case uint ui: writer.WriteNumberValue(ui); return;
            case ulong ul: writer.WriteNumberValue(ul); return;
            case ushort us: writer.WriteNumberValue(us); return;
            case sbyte sb: writer.WriteNumberValue(sb); return;
            case Half h: writer.WriteRawValue(h.ToString(CultureInfo.InvariantCulture)); return;
            case Int128 i128: writer.WriteRawValue(i128.ToString(CultureInfo.InvariantCulture)); return;
            case UInt128 u128: writer.WriteRawValue(u128.ToString(CultureInfo.InvariantCulture)); return;

            case Guid g:
                writer.WriteStringValue(g);
                return;

            case DateTime dt:
                writer.WriteStringValue(dt);
                return;

            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                return;

            case DateOnly date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return;

            case TimeOnly time:
                writer.WriteStringValue(time.ToTimeSpan().ToString("c", CultureInfo.InvariantCulture));
                return;

            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                return;

            case IDictionary dictionary:
                WriteDictionary(writer, dictionary, depth);
                return;

            case IEnumerable sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                {
                    Write(writer, item, depth + 1);
                }

                writer.WriteEndArray();
                return;

            default:
                throw UnsupportedType(value.GetType());
        }
    }

    private static void WriteEnum(Utf8JsonWriter writer, Enum value)
    {
        if (value.GetTypeCode() is TypeCode.UInt64)
        {
            writer.WriteNumberValue(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
    }

    private static void WriteDictionary(Utf8JsonWriter writer, IDictionary dictionary, int depth)
    {
        var entries = new List<KeyValuePair<string, object?>>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new ArgumentException(
                    $"Query key dictionaries must have string keys, but '{dictionary.GetType()}' " +
                    $"has a key of type '{entry.Key.GetType()}'.");
            }

            entries.Add(new(key, entry.Value));
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        writer.WriteStartObject();
        foreach (var (key, entryValue) in entries)
        {
            writer.WritePropertyName(key);
            Write(writer, entryValue, depth + 1);
        }

        writer.WriteEndObject();
    }

    private static ArgumentException UnsupportedType(Type type) => new(
        $"Type '{type}' cannot be used in a query key. Query key elements must be null, " +
        "strings, booleans, numbers, enums, Guids, dates and times, sequences, or " +
        "string-keyed dictionaries. Use a Dictionary<string, object?> in place of an " +
        "anonymous object, or implement IQueryKeyElement on your own type.");
}
