namespace RabstackQuery;

/// <summary>
/// Data-availability status of a query. Indicates whether the query has data, an error, or neither.
/// </summary>
public enum QueryStatus
{
    /// <summary>The query has no data yet and has not errored.</summary>
    Pending,

    /// <summary>The most recent fetch resulted in an error.</summary>
    Errored,

    /// <summary>The query has successfully received data at least once.</summary>
    Succeeded,
}
