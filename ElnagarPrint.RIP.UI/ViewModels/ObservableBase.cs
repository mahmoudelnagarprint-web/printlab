using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ElnagarPrint.RIP.UI.ViewModels;

/// <summary>
/// قاعدة لكل الـ ViewModels — إشعار الربط ثنائي الاتجاه (Two-way binding)
/// </summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// تعيين القيمة مع إشعار تلقائي فقط عند التغيير الفعلي
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}