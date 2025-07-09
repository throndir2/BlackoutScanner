using System;
using System.Globalization;
using System.Windows.Data;
using BlackoutScanner.Models;

namespace BlackoutScanner.Converters
{
    public class ProfileComparisonConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is GameProfile profile &&
                values[1] is GameProfile activeProfile)
            {
                // Compare by ProfileName since that should be unique
                return profile.ProfileName == activeProfile.ProfileName;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
