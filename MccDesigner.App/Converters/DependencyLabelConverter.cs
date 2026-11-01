using System.Globalization;
using System.Windows.Data;
namespace MccDesigner.Converters;

public sealed class DependencyLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is Guid id && values[1] is IReadOnlyDictionary<Guid, string> labels && labels.TryGetValue(id, out var label)
            ? label : "—";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
