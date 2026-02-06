using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TaskStatus = TaskerCore.Models.TaskStatus;

namespace TaskerTray.Converters;

public class CheckedToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TaskStatus status)
        {
            return status switch
            {
                TaskStatus.Done => new SolidColorBrush(Color.Parse("#666666")), // Dimmed for done
                _ => new SolidColorBrush(Color.Parse("#FFFFFF")) // White for pending/in-progress
            };
        }
        // Backward compat: bool binding
        if (value is bool isChecked && isChecked)
        {
            return new SolidColorBrush(Color.Parse("#666666"));
        }
        return new SolidColorBrush(Color.Parse("#FFFFFF"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
