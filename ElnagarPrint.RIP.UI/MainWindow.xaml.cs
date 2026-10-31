using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ElnagarPrint.RIP.Core;
using ElnagarPrint.RIP.UI.Models;
using ElnagarPrint.RIP.UI.Services;
using ElnagarPrint.RIP.UI.ViewModels;
using Microsoft.Win32;

namespace ElnagarPrint.RIP.UI;

/// <summary>
/// النافذة الرئيسية — تربط التبويبات السبعة بالمحرك وتدير الطباعة غير المتزامنة
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private HalftoneResult? _lastHalftone;
    private CancellationTokenSource? _previewCts;

    public MainWindow()
    {
        InitializeComponent();

        // ربط الـ ViewModel بكل التبويبات السبعة (نفس الكائن)
        TabMain.DataContext      = _vm;
        TabLayout.DataContext    = _vm;
        TabColor.DataContext     = _vm;
        TabCorrection.DataContext= _vm;
        TabConsumption.DataContext = _vm;
        TabInkCtrl.DataContext   = _vm;
        TabOptions.DataContext   = _vm;

        // السجلات
        _vm.LogAdded += (_, line) => AppendLog(line);

        // المعاينة
        _vm.PreviewReady += (_, halftone) =>
        {
            _lastHalftone = halftone;
            UpdatePreview();
        };

        UpdatePrinterStatus(initial: true);
    }

    /// <summary>
    /// 🔌 زر «اختبار» — تشخيص كامل لمسارات الاتصال بالطابعة (Stage 4)
    /// LibUsb registry → طوابير Windows → فتح USB تجريبي → صلاحية الطابور
    /// </summary>
    private void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        _vm.Log("🔌 بدء تشخيص الاتصال بالطابعة…");

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var report = UsbDiagnostics.RunFullTest(sendProbe: false);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var line in report.ToString().Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line))
                            _vm.Log(line.TrimEnd());

                    if (report.HasWorkingTransport)
                    {
                        PrinterStatusText.Text = $"الطابعة: جاهزة عبر {report.WorkingTransport}";
                        PrinterStatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessGreenBrush");
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    _vm.Log($"❌ التشخيص فشل: {ex.Message}"));
            }
        });
    }

    /// <summary>مرّر نتيجة المعاينة لحاسبة الاستهلاك — QuickEstimate</summary>
    private void UpdateConsumptionEstimate(string imagePath)
    {
        try
        {
            // تقدير سريع من الصورة — كثافة افتراضية 0.5 (تُحدَّث من الـ halftone في Stage 4)
            _vm.LastCost = InkCostEstimator.QuickEstimate(imagePath, inkDensityFactor: 0.5);
            _vm.Log($"💧 تقدير الاستهلاك: {_vm.CostTotalMl} — {_vm.CostTotalEgp}");
        }
        catch (Exception ex)
        {
            _vm.Log($"⚠ تقدير الاستهلاك فشل: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  السجل — Status Log
    // ════════════════════════════════════════════════════════════════════

    private void AppendLog(string line)
    {
        StatusLog.AppendText(line + Environment.NewLine);
        StatusLog.ScrollToEnd();
    }

    // ════════════════════════════════════════════════════════════════════
    //  اختيار الصورة
    // ════════════════════════════════════════════════════════════════════

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title   = "اختر صورة للطباعة",
            Filter  = "صور|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|كل الملفات|*.*"
        };

        if (dlg.ShowDialog(this) == true)
        {
            _vm.CurrentImagePath = dlg.FileName;
            _vm.Log($"📂 تم اختيار: {_vm.CurrentImageName}");
            ShowSourcePreview(dlg.FileName);
        }
    }

    /// <summary>معاينة الصورة الأصلية (قبل التنقيط) في لوحة المعاينة</summary>
    private void ShowSourcePreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            PreviewImage.Source   = bmp;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            var fi = new FileInfo(path);
            PreviewInfoText.Text  = $"{Path.GetFileName(path)} — {bmp.PixelWidth}×{bmp.PixelHeight}px — {fi.Length / 1024.0:F0} KB";
        }
        catch (Exception ex)
        {
            _vm.Log($"❌ تعذر تحميل المعاينة: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  المعاينة (بدون طباعة) — يشغّل pipeline حتى مرحلة التنقيط
    // ════════════════════════════════════════════════════════════════════

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.CurrentImagePath) || !File.Exists(_vm.CurrentImagePath))
        {
            MessageBox.Show(this, "اختر صورة أولاً.", "النجار برنت RIP",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        _vm.Log("🔍 بدء المعاينة (بدون طباعة)…");
        _vm.ProgressText = "جارٍ المعاينة…";
        PrintButton.IsEnabled = false;

        try
        {
            var imagePath = _vm.CurrentImagePath;
            var settings  = _vm.BuildPipelineSettings();

            // 🚀 الـ pipeline في خيط خلفي — حتى المعاينة لا تتجمد
            var halftone = await Task.Run(() =>
            {
                // Pipeline settings نفسها تُبنى، لكن نستخدم PreviewHalftone الذي
                // يمر بالمراحل 1→3 فقط (فصل ألوان + تنقيط) بدون ترميز أو USB
                var pipeline = new RipPipeline(settings);
                return pipeline.PreviewHalftone(imagePath);
            }, token);

            _vm.RaisePreviewReady(halftone);
            _vm.Log("✅ المعاينة جاهزة — استخدم قائمة «القناة» لعرض كل قناة.");
            UpdateConsumptionEstimate(imagePath);
        }
        catch (OperationCanceledException) { _vm.Log("🛑 أُلغيت المعاينة."); }
        catch (Exception ex)              { _vm.Log($"❌ فشل المعاينة: {ex.Message}"); }
        finally
        {
            PrintButton.IsEnabled = true;
            _vm.ProgressText = "جاهز";
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  تحديث لوحة المعاينة حسب القناة المختارة
    // ════════════════════════════════════════════════════════════════════

    private void PreviewChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdatePreview();

    private void UpdatePreview()
    {
        if (_lastHalftone is null) return;

        int idx = PreviewChannelCombo?.SelectedIndex is int i && i >= 1 ? i - 1 : -1;

        var small = HalftonePreviewRenderer.Render(_lastHalftone, idx);
        PreviewImage.Source = HalftonePreviewRenderer.Upscale(small, 320)
                              ?? small;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        PreviewInfoText.Text = $"معاينة منقّطة {_lastHalftone.Width}×{_lastHalftone.Height}px — " +
                               (idx < 0 ? "توليفية (6 قنوات)" : new[]{"Cyan","Magenta","Yellow","Black","LightCyan","LightMagenta"}[idx]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  الطباعة — async عبر AsyncPrintCommand
    // ════════════════════════════════════════════════════════════════════

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanPrint)
        {
            MessageBox.Show(this, "اختر صورة أولاً.", "النجار برنت RIP",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CancelPrintButton.IsEnabled = true;
        PrintButton.IsEnabled = false;

        // ⏳ تشغيل الـ pipeline في خيط خلفي عبر الأمر غير المتزامن
        _vm.PrintCommand.Execute(null);

        // انتظار انتهاء الأمر لتحديث الأزرار
        while (_vm.IsBusy)
            await Task.Delay(200);

        CancelPrintButton.IsEnabled = false;
        PrintButton.IsEnabled = true;

        // تحديث حاسبة الاستهلاك بعد الطباعة
        if (!string.IsNullOrEmpty(_vm.CurrentImagePath))
            UpdateConsumptionEstimate(_vm.CurrentImagePath);
    }

    private void CancelPrint_Click(object sender, RoutedEventArgs e)
    {
        // إلغاء عبر الأمر ( CancellationToken )
        if (_vm.PrintCommand is AsyncPrintCommand cmd)
            cmd.Cancel();
        _vm.Log("🛑 طُلب الإلغاء…");
    }

    // ════════════════════════════════════════════════════════════════════
    //  وضع الورشة السريع — Quick Workshop Mode ⚡
    // ════════════════════════════════════════════════════════════════════

    private void QuickMode_Changed(object sender, RoutedEventArgs e)
    {
        bool quick = QuickModeToggle?.IsChecked == true;

        // إخفاء التبويبات المتقدمة — يبقى Main + Consumption + Ink Ctrl
        if (MainTabs != null)
        {
            foreach (TabItem tab in MainTabs.Items.OfType<TabItem>())
            {
                var header = tab.Header?.ToString() ?? "";
                bool keepVisible = header is "Main" or "Consumption" or "Ink Ctrl";
                tab.Visibility = quick && !keepVisible ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // إخفاء لوحة المعاينة الجانبية في الوضع السريع لتوسيع مساحة العمل
        if (PreviewColumn != null)
            PreviewColumn.Width = quick ? new GridLength(0) : new GridLength(340);

        _vm?.Log(quick
            ? "⚡ وضع الورشة السريع: مُفعّل — 3 تبويبات فقط + معاينة مخفية"
            : "🔧 الوضع الاحترافي الكامل: 7 تبويبات");
    }

    // ════════════════════════════════════════════════════════════════════
    //  حالة الطابعة
    // ════════════════════════════════════════════════════════════════════

    private void UpdatePrinterStatus(bool initial = false)
    {
        // الفحص في خيط خلفي — لا يجمّد الواجهة عند بدء التشغيل
        Task.Run(() =>
        {
            DiscoveredPrinter[]? printers = null;
            try { printers = UsbPrinterPort.DiscoverL1800(); } catch { /* بدون LibUsb */ }

            Dispatcher.BeginInvoke(() =>
            {
                bool found = printers is { Length: > 0 };

                PrinterStatusDot.Fill = found
                    ? (System.Windows.Media.Brush)FindResource("SuccessGreenBrush")
                    : (System.Windows.Media.Brush)FindResource("TextMutedBrush");

                PrinterStatusText.Text = found
                    ? $"الطابعة: {printers![0].Name} ✓"
                    : "الطابعة: غير متصلة";

                if (found) _vm.Log($"🖨 اتصلت طابعة: {printers![0].Name}");
            });
        });
    }
}