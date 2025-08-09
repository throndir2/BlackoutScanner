using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BlackoutScanner.Models;

namespace BlackoutScanner.Converters
{
    public class ComparisonModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CategoryComparisonMode mode)
            {
                return mode == CategoryComparisonMode.Text ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
