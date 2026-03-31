namespace RabstackQuery;

/// <summary>
/// Direction of an infinite query page fetch.
/// </summary>
public enum FetchDirection
{
    /// <summary>Fetch the next page after the current data.</summary>
    Forward,

    /// <summary>Fetch the previous page before the current data.</summary>
    Backward
}
