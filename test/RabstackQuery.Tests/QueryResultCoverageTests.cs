using System.Reflection;

namespace RabstackQuery;

/// <summary>
/// Guards against adding a property to <see cref="IQueryResult{TData}"/> without
/// updating the comparer (<see cref="QueryResultComparer"/>), tracking wrapper
/// (<see cref="TrackedQueryResult{TData}"/>), and constants (<see cref="QueryResultProps"/>).
/// </summary>
public class QueryResultCoverageTests
{
    private static readonly HashSet<string> ExcludedProperties = [
        nameof(IQueryResult<object>.RefetchAsync),
    ];

    private static HashSet<string> GetInterfacePropertyNames()
    {
        return typeof(IQueryResult<object>)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !ExcludedProperties.Contains(name))
            .ToHashSet();
    }

    private static HashSet<string> GetConstantValues()
    {
        return typeof(QueryResultProps)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();
    }

    private static HashSet<string> GetTrackedPropertyNames()
    {
        return typeof(TrackedQueryResult<object>)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !ExcludedProperties.Contains(name))
            .ToHashSet();
    }

    [Fact]
    public void QueryResultProps_CoversAllInterfaceProperties()
    {
        var interfaceProps = GetInterfacePropertyNames();
        var constantProps = GetConstantValues();

        var missing = interfaceProps.Except(constantProps).ToList();
        Assert.True(missing.Count == 0,
            $"QueryResultProps is missing constants for: {string.Join(", ", missing)}");

        var extra = constantProps.Except(interfaceProps).ToList();
        Assert.True(extra.Count == 0,
            $"QueryResultProps has constants for removed properties: {string.Join(", ", extra)}");
    }

    [Fact]
    public void TrackedQueryResult_CoversAllInterfaceProperties()
    {
        var interfaceProps = GetInterfacePropertyNames();
        var trackedProps = GetTrackedPropertyNames();

        var missing = interfaceProps.Except(trackedProps).ToList();
        Assert.True(missing.Count == 0,
            $"TrackedQueryResult is missing wrappers for: {string.Join(", ", missing)}");
    }
}
