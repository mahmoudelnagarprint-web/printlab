using System.Globalization;
using System.Windows.Data;

namespace ElnagarPrint.RIP.UI.Converters;

/// <summary>
/// محوّل الراديو أزرار للـ Enums — يقارن قيمة الـ enum مع Parameter
/// IsChecked="{Binding Prop, Converter={StaticResource EnumEqualConverter}, ConverterParameter=Two}"
/// </summary>
public class EnumEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b || !b || parameter is null || targetType is null)
            return Binding.DoNothing;

        try
        {
            // targetType قد يكون enum أو Nullable<enum>
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return Enum.Parse(t, parameter.ToString()!, ignoreCase: true);
        }
        catch
        {
            return Binding.DoNothing;
        }
    }
}