using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        // 默认 正常为绿，异常为红
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isReady && isReady)
            {
                return Brushes.LimeGreen; // 或使用 #00FF00
            }
            return Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}