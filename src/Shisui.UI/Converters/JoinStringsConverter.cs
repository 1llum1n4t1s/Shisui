using System.Globalization;
using Avalonia.Data.Converters;

namespace Shisui.UI.Converters;

public sealed class JoinStringsConverter : IValueConverter
{
    public static readonly JoinStringsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<string> items
            ? (items.Any() ? string.Join(", ", items) : "(なし)")
            : "(なし)";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
