using System;
using System.Globalization;
using System.Windows.Data;

namespace ElnagarPrint.RIP.UI.Converters;

/// <summary>عكس قيمة منطقية — لتعطيل السلايدر عندما يكون القفل مفعّلًا</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
