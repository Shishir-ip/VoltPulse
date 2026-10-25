using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Converters;

/// <summary>Converts a brush resource key (e.g. "BrushStateCharging") into the live brush.</summary>
public sealed class BrushKeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string key
            ? System.Windows.Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Multiplies a 0..1 fraction by the given pixel width (ConverterParameter).</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        double width = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return width * fraction;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>BatteryLiveState to a brush resource key, honoring the "use state colors" rule handled by the VM.</summary>
public sealed class LiveStateDebugConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is BatteryLiveState s ? s.ToString() : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>True becomes false and vice versa (used for IsEnabled while importing).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
