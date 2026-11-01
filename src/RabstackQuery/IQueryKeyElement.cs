namespace RabstackQuery;

/// <summary>
/// Represents a custom <see cref="QueryKey"/> element encoded through <see cref="ToQueryKeyValue"/>.
/// </summary>
/// <remarks>
/// <para>
/// Supported values are <see langword="null"/>, strings, chars, booleans, numbers, enums,
/// <see cref="Guid"/> values, dates and times, sequences, and string-keyed dictionaries.
/// </para>
/// <para>
/// Two elements are the same key when their <see cref="ToQueryKeyValue"/> results encode
/// identically, regardless of their runtime types.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record TodoFilter(string Status, int Page) : IQueryKeyElement
/// {
///     public object ToQueryKeyValue() =&gt;
///         new Dictionary&lt;string, object?&gt; { ["status"] = Status, ["page"] = Page };
/// }
/// </code>
/// </example>
public interface IQueryKeyElement
{
    /// <summary>
    /// Returns the value that identifies this element in a query key.
    /// </summary>
    /// <returns>
    /// A value of a supported shape, or another <see cref="IQueryKeyElement"/>. Values nested
    /// in sequences and dictionaries follow the same rules.
    /// </returns>
    object? ToQueryKeyValue();
}
