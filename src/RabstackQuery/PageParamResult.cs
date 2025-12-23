namespace RabstackQuery;

/// <summary>
/// Wraps page param values to distinguish "no more pages" from valid page params
/// like <c>0</c> or <c>null</c> (for reference-type TPageParam). Required because
/// <c>TPageParam?</c> on an unconstrained generic doesn't produce <c>Nullable&lt;T&gt;</c>
/// for value types.
/// </summary>
public readonly record struct PageParamResult<T>
{
    public T Value { get; }
    public bool HasValue { get; }

    private PageParamResult(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>Creates a result indicating a page param is available.</summary>
    public static PageParamResult<T> Some(T value) => new(value);

    /// <summary>Indicates no more pages are available.</summary>
    public static PageParamResult<T> None => default;

    /// <summary>
    /// Implicit conversion from <typeparamref name="T"/> so callers can
    /// <c>return nextCursor</c> directly for "has next page".
    /// </summary>
    public static implicit operator PageParamResult<T>(T value) => Some(value);
}
