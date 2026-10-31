using System.Windows;

namespace ElnagarPrint.RIP.UI;

/// <summary>
/// نقطة دخول التطبيق — معالجة الاستثناءات العامّة
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // معالجة الاستثناءات العامّة
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"خطأ غير متوقع:\n{ex?.Message}",
                "خطأ Fatal — النجار برنت RIP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"خطأ في الواجهة:\n{args.Exception.Message}",
                "خطأ — النجار برنت RIP",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}