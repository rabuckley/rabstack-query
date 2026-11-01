using System.Reflection;
using System.Text;

using RabstackQuery.Mvvm;

namespace RabstackQuery.Blazor;

public sealed class AdapterParityTests
{
    private static readonly string[] FactoryNames =
        ["UseQuery", "UseQueryCollection", "UseInfiniteQuery", "UseMutation"];

    [Fact]
    public void PageViewModel_MirrorsQueryClientExtensions()
    {
        var expected = ExtensionSignatures();

        var actual = AdapterSignatures(typeof(PageViewModel));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RabstackComponentBase_MirrorsQueryClientExtensions()
    {
        var expected = ExtensionSignatures();

        var actual = AdapterSignatures(typeof(RabstackComponentBase));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QueryClientExtensions_ExposesPlannedFactorySurface()
    {
        // Keep the inference shortcuts limited to five query, five collection, one infinite
        // and three mutation overloads.
        var signatures = ExtensionSignatures();

        Assert.Equal(14, signatures.Count);
    }

    private static List<string> ExtensionSignatures() =>
        typeof(QueryClientExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => FactoryNames.Contains(m.Name))
            .Select(m => Describe(m, m.GetParameters().Skip(1)))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static List<string> AdapterSignatures(Type adapter) =>
        adapter
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsFamily && FactoryNames.Contains(m.Name))
            .Select(m => Describe(m, m.GetParameters()))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Describe(MethodInfo method, IEnumerable<ParameterInfo> parameters)
    {
        var builder = new StringBuilder();
        builder.Append(FormatType(method.ReturnType)).Append(' ').Append(method.Name);

        var generics = method.GetGenericArguments();
        if (generics.Length > 0)
        {
            builder.Append('<')
                .AppendJoin(", ", generics.Select(FormatConstraints))
                .Append('>');
        }

        builder.Append('(')
            .AppendJoin(", ", parameters.Select(FormatParameter))
            .Append(')');
        return builder.ToString();
    }

    private static string FormatParameter(ParameterInfo parameter)
    {
        var text = FormatType(parameter.ParameterType);
        return parameter.IsOptional ? $"{text} = {parameter.DefaultValue ?? "null"}" : text;
    }

    private static string FormatConstraints(Type genericParameter)
    {
        var constraints = genericParameter.GetGenericParameterConstraints().Select(FormatType).ToList();
        var attributes = genericParameter.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
        if (attributes is not GenericParameterAttributes.None)
            constraints.Add(attributes.ToString());

        var name = FormatType(genericParameter);
        return constraints.Count is 0 ? name : $"{name} : {string.Join(" & ", constraints)}";
    }

    private static string FormatType(Type type)
    {
        if (type.IsGenericParameter)
            return $"!!{type.GenericParameterPosition}";

        if (type.IsArray)
            return $"{FormatType(type.GetElementType()!)}[]";

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition();
        return $"{definition.FullName}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }
}
