using System.Globalization;
using System.Windows.Data;

namespace ClaudeUsageTray.Presentation.Converters;

public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0.0;
        if (values[0] is not double pct) return 0.0;
        if (values[1] is not double totalWidth) return 0.0;
        return Math.Max(0, Math.Min(totalWidth, totalWidth * pct / 100.0));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
