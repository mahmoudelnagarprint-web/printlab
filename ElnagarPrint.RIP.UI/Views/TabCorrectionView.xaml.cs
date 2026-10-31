using System.Windows;
using System.Windows.Controls;

namespace ElnagarPrint.RIP.UI.Views;

public partial class TabCorrectionView : UserControl
{
    public TabCorrectionView() => InitializeComponent();

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.CorrCyan = 100;
            vm.CorrMagenta = 100;
            vm.CorrYellow = 100;
            vm.CorrBlack = 100;
            vm.Log("↩ أُعيد ضبط التصحيح اليدوي لكل القنوات إلى 100%");
        }
    }
}