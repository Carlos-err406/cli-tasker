using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskerTray.Converters;

public class CheckedToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked)
        {
            return new SolidColorBrush(Color.Parse("#666666")); // Dimmed for completed
        }
        return new SolidColorBrush(Color.Parse("#FFFFFF")); // White for pending
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
