using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace yShorts.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = false;
        if (value is int count && count > 0) isVisible = true;
        if (value is bool b && b) isVisible = true;

        if (parameter?.ToString() == "Invert")
            isVisible = !isVisible;

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
