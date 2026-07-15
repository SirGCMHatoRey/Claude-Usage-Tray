using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ClaudeUsageTray.Domain.Models;
using VisualState = ClaudeUsageTray.Domain.Models.VisualState;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;

namespace ClaudeUsageTray.Presentation.Converters;

public sealed class VisualStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VisualState state) return WpfBrushes.Gray;
        return state switch
        {
            VisualState.Normal => new SolidColorBrush(WpfColor.FromRgb(0x4C, 0xC9, 0x74)),      // green
            VisualState.Warning => new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xB9, 0x00)),     // amber
            VisualState.Critical => new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x45, 0x45)),    // red
            VisualState.Loading => new SolidColorBrush(WpfColor.FromRgb(0x60, 0xCD, 0xEA)),     // blue
            VisualState.Disconnected => new SolidColorBrush(WpfColor.FromRgb(0x88, 0x88, 0x88)), // gray
            VisualState.Error => new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x45, 0x45)),       // red
            _ => WpfBrushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
