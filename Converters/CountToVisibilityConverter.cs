using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace yShorts.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter?.ToString() == "RatioToMargin" && value is double ratio)
        {
            // This is a hacky way to use one converter for multiple things. 
            // In a real app we'd have multiple converters.
            return new Thickness(ratio * 700, 0, 0, 0); // 700 is a safe min-width guess
        }

        bool isVisible = false;
        if (value is int count && count > 0) isVisible = true;
        if (value is bool b && b) isVisible = true;
        if (value is double d && d > 0) isVisible = true;

        if (parameter?.ToString() == "Invert")
            isVisible = !isVisible;

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
