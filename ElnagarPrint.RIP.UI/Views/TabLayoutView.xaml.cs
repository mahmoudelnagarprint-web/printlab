using System.Windows;
using System.Windows.Controls;
using ElnagarPrint.RIP.UI.Models;
using ElnagarPrint.RIP.UI.ViewModels;

namespace ElnagarPrint.RIP.UI.Views;

public partial class TabLayoutView : UserControl
{
    public TabLayoutView() => InitializeComponent();

    private void CopiesMinus_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Copies > 1)
            vm.Copies--;
    }

    private void CopiesPlus_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Copies++;
    }
}