using System.Windows;
using System.Windows.Controls;

namespace ElnagarPrint.RIP.UI.Views;

public partial class TabInkCtrlView : UserControl
{
    public TabInkCtrlView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.PropertyChanged += (_, _) => UpdateGates(vm);
                UpdateGates(vm);
            }
        };
    }

    private void ZeroLightInks_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.ZeroLightInks();
            UpdateGates(vm);
            vm.Log("🔒 قُفلت قنوات Light Magenta و Light Cyan — حبر الفاتح = 0 فعليًا عند المحرك.");
        }
    }

    private void RestoreLightInks_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.RestoreLightInks();
            UpdateGates(vm);
            vm.Log("↩ فُكّ قفل الألوان الفاتحة وأُعيدت لـ 100%.");
        }
    }

    private void ShowGates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            UpdateGates(vm);
    }

    private void UpdateGates(ViewModels.MainViewModel vm)
    {
        string lc = vm.LockLightCyan ? "🔒 مقفولة (0%)"
                                     : $"{vm.EffectiveLightCyanGate:P0}";
        string lm = vm.LockLightMagenta ? "🔒 مقفولة (0%)"
                                        : $"{vm.EffectiveLightMagentaGate:P0}";
        GatesStatusText.Text = $"حالة الـ Gates (المُمرَّرة للمحرك): Lc={lc} | Lm={lm}";
    }
}