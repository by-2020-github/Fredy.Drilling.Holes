using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush ReadyBrush = CreateBrush("#2E7D32");
        private static readonly SolidColorBrush NotReadyBrush = CreateBrush("#D32F2F");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isReady && isReady)
            {
                return ReadyBrush;
            }

            return NotReadyBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static SolidColorBrush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}