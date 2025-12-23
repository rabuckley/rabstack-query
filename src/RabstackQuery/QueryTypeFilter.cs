namespace RabstackQuery;

/// <summary>
/// Determines which queries pass the "type" filter based on observer count.
/// </summary>
public enum QueryTypeFilter
{
    /// <summary>All queries regardless of observer count.</summary>
    All,

    /// <summary>Only queries with at least one observer.</summary>
    Active,

    /// <summary>Only queries with zero observers.</summary>
    Inactive
}
