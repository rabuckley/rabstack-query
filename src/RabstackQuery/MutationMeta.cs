namespace RabstackQuery;

/// <summary>
/// Metadata for arbitrary key-value storage with mutations.
/// </summary>
public sealed class MutationMeta
{
    private readonly IReadOnlyDictionary<string, object?> _data;

    /// <summary>
    /// Creates a new empty MutationMeta instance.
    /// </summary>
    public MutationMeta() : this(new Dictionary<string, object?>()) { }

    /// <summary>
    /// Creates a new MutationMeta instance with the provided data.
    /// </summary>
    public MutationMeta(IReadOnlyDictionary<string, object?> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    /// <summary>
    /// Gets the value associated with the specified key, or null if not found.
    /// </summary>
    public object? this[string key] => _data.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Attempts to get the value associated with the specified key.
    /// </summary>
    public bool TryGetValue(string key, out object? value) => _data.TryGetValue(key, out value);

    /// <summary>
    /// Gets all keys in this metadata.
    /// </summary>
    public IEnumerable<string> Keys => _data.Keys;

    /// <summary>
    /// Gets the number of key-value pairs in this metadata.
    /// </summary>
    public int Count => _data.Count;
}
