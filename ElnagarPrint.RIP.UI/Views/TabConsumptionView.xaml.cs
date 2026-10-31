using System.Windows;
using System.Windows.Controls;

namespace ElnagarPrint.RIP.UI.Views;

public partial class TabConsumptionView : UserControl
{
    public TabConsumptionView() => InitializeComponent();

    private void ResetMeters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.ResetMeters();
            vm.Log("🗑 صُفِّرت عدادات الاستهلاك والتكلفة.");
        }
    }
}