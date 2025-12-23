using System.Globalization;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Converts hex color strings (e.g. "#22C55E") to MAUI <see cref="Color"/> values.
/// Used by data templates that bind to <see cref="DevTools.QueryListItem.StatusColorHex"/>
/// and <see cref="DevTools.MutationListItem.StatusColorHex"/>.
/// </summary>
internal sealed class HexToColorConverter : IValueConverter
{
    internal static readonly HexToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex ? Color.FromArgb(hex) : Colors.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
