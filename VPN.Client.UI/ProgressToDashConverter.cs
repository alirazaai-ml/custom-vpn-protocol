using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VPN.Client.UI
{
    public class ProgressToDashConverter : IValueConverter
    {
        // Converts a progress value (0-100) to a StrokeDashArray for circular progress
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double progress = 0;
            if (value is double d)
                progress = Math.Max(0, Math.Min(100, d));

            // The total length of the circle (circumference) is normalized to 100 units
            // The dash is the progress, the gap is the remainder
            double dash = progress;
            double gap = 100 - progress;
            return new DoubleCollection { dash, gap };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}