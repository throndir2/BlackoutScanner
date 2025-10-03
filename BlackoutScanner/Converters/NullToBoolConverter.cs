using System;
using System.Globalization;
using System.Windows.Data;

namespace BlackoutScanner.Converters
{
    /// <summary>
    /// Converts null to false, non-null to true.
    /// Used for enabling buttons when an item is selected.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
