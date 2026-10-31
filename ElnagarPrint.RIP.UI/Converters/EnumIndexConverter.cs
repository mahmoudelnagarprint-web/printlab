using System.Globalization;
using System.Windows.Data;

namespace ElnagarPrint.RIP.UI.Converters;

/// <summary>
/// محوّل عام: Enum ↔ SelectedIndex للـ ComboBox
/// • أثناء العرض: enum → index ترتيبه في تعريف الـ enum
/// • أثناء التحرير: index → قيمة الـ enum المقابلة
/// يعمل مع أي enum مرتبة الـ ComboBoxItems بترتيب تعريفه
/// </summary>
public class EnumIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return 0;
        var t = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (!t.IsEnum) return 0;
        var values = Enum.GetValues(t);
        for (int i = 0; i < values.Length; i++)
            if (values.GetValue(i)!.Equals(value)) return i;
        return 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int idx || idx < 0) return Binding.DoNothing;

        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!t.IsEnum) return Binding.DoNothing;

        var values = Enum.GetValues(t);
        return idx < values.Length
            ? values.GetValue(idx)!
            : Binding.DoNothing;
    }
}