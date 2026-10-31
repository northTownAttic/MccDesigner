using System.Globalization;
using System.Windows.Data;

namespace MccDesigner.Converters;

public sealed class OperationEndTimeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is Guid id && values[1] is IReadOnlyDictionary<Guid, double> endTimes && endTimes.TryGetValue(id, out var time)
            ? time.ToString("0.###", culture) : "—";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("계산된 완료 시각은 읽기 전용입니다.");
}
