using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElnagarPrint.RIP.Core;
using ElnagarPrint.RIP.UI.Models;
using ElnagarPrint.RIP.UI.Services;
using ElnagarPrint.RIP.UI.ViewModels;
using ElnagarPrint.RIP.UI.Views;
using Microsoft.Win32;

namespace ElnagarPrint.RIP.UI;

/// <summary>
/// النافذة الرئيسية — لوحة رئيسية بثلاثة أعمدة + تنقّل سفلي (تصميم v4)
/// الرئيسية: معاينة بسويتشات القنوات | إعدادات سريعة + قنوات فاتحة + ورق | حالة + حلقة تقدم
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private HalftoneResult? _lastHalftone;
    private CancellationTokenSource? _previewCts;
    private int _previewChannelIndex = -1;   // -1 = توليفية
    private int _activeSwatch = -1;

    // المعاينة الحية — أي تحريك سلايدر يُحدّث المعاينة فورًا
    private DispatcherTimer? _livePreviewTimer;
    private CancellationTokenSource? _liveCts;
    private int _liveRunId;

    private static readonly HashSet<string> LivePreviewTriggers = new()
    {
        nameof(MainViewModel.Density),     nameof(MainViewModel.Brightness),
        nameof(MainViewModel.Contrast),    nameof(MainViewModel.Saturation),
        nameof(MainViewModel.CorrCyan),    nameof(MainViewModel.CorrMagenta),
        nameof(MainViewModel.CorrYellow),  nameof(MainViewModel.CorrBlack),
        nameof(MainViewModel.LcInkPercent),nameof(MainViewModel.LmInkPercent),
        nameof(MainViewModel.LockLightCyan), nameof(MainViewModel.LockLightMagenta)
    };

    // صفحات التنقّل — تُنشأ مرة واحدة
    private readonly TabLayoutView     _tabLayout     = new();
    private readonly TabColorView      _tabColor      = new();
    private readonly TabInkCtrlView    _tabInkCtrl    = new();
    private readonly TabCorrectionView _tabCorrection = new();
    private readonly TabOptionsView    _tabOptions    = new();

    public MainWindow()
    {
        InitializeComponent();

        // ⚖️ مقاس متكيّف مع أي شاشة — 92% من مساحة العمل مع حد أقصى معقول
        var work = SystemParameters.WorkArea;
        Width  = Math.Min(1500, work.Width  * 0.92);
        Height = Math.Min(900,  work.Height * 0.92);
        Left = (work.Width  - Width)  / 2 + work.Left;
        Top  = (work.Height - Height) / 2 + work.Top;

        // ربط الـ ViewModel بكل الصفحات
        _tabLayout.DataContext      = _vm;
        _tabColor.DataContext       = _vm;
        _tabInkCtrl.DataContext     = _vm;
        _tabCorrection.DataContext  = _vm;
        _tabOptions.DataContext     = _vm;

        // السجلات
        _vm.LogAdded += (_, line) => AppendLog(line);

        // المعاينة
        _vm.PreviewReady += (_, halftone) =>
        {
            _lastHalftone = halftone;
            UpdatePreview();
        };

        // تتبّع تقدم الطباعة — الحلقة الدائرية
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressPercent))
                UpdateProgressRing(_vm.ProgressPercent);
        };

        // المعاينة الحية — تحريك أي سلايدر يُعيد المعاينة تلقائيًا (بعد 300ms سكون)
        _livePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _livePreviewTimer.Tick += (_, _) => { _livePreviewTimer.Stop(); _ = RunLivePreviewAsync(); };
        _vm.PropertyChanged += VmLivePreview_PropertyChanged;

        UpdateRingGeometry(0);
        UpdatePrinterStatus(initial: true);
    }

    // ════════════════════════════════════════════════════════════════════
    //  الحلقة الدائرية للتقدم — Path arc
    // ════════════════════════════════════════════════════════════════════

    private void UpdateProgressRing(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        RingPercentText.Text = $"{pct}%";
        UpdateRingGeometry(pct);
    }

    private void UpdateRingGeometry(int pct)
    {
        const double size = 150;      // قطر الحلقة
        const double r = 65;          // نصف القطر
        var center = new Point(size / 2, size / 2);

        // حلقة الخلفية — دائرة كاملة
        RingBackground.Data = Geometry.Parse(
            $"M {size / 2 - r},{size / 2} a {r},{r} 0 1 0 {r * 2},0 a {r},{r} 0 1 0 {-r * 2},0");

        if (pct <= 0)
        {
            RingProgress.Data = Geometry.Empty;
            return;
        }

        // قوس التقدم — يبدأ من الأعلى ويلتف باتجاه عقارب الساعة
        double sweep = pct / 100.0 * 360.0;
        double startAngle = -90;
        double endAngle = startAngle + sweep;

        var start = PointFromAngle(center, r, startAngle);
        var end = PointFromAngle(center, r, endAngle);
        bool isLarge = sweep > 180;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        RingProgress.Data = geo;
    }

    private static Point PointFromAngle(Point c, double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    // ════════════════════════════════════════════════════════════════════
    //  التنقّل السفلي — تبديل لوحة الرئيسية / الصفحات
    // ════════════════════════════════════════════════════════════════════

    private void Nav_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string key) return;
        if (PageHost is null || HomePanel is null) return;   // أثناء تهيئة XAML

        if (key == "home")
        {
            HomePanel.Visibility = Visibility.Visible;
            PageHost.Content = null;
            PageHost.Visibility = Visibility.Collapsed;
        }
        else
        {
            HomePanel.Visibility = Visibility.Collapsed;
            PageHost.Visibility = Visibility.Visible;
            PageHost.Content = key switch
            {
                "layout"     => _tabLayout,
                "color"      => _tabColor,
                "ink"        => _tabInkCtrl,
                "correction" => _tabCorrection,
                "options"    => _tabOptions,
                _            => null
            };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  سويتشات القنوات — ضغطة: قناة، ضغطة ثانية على نفسها: توليفي
    // ════════════════════════════════════════════════════════════════════

    private void ChannelSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        int idx = int.Parse(tag);

        _previewChannelIndex = (_activeSwatch == idx) ? -1 : idx;
        _activeSwatch = (_previewChannelIndex == -1) ? -1 : idx;
        UpdatePreview();
    }

    /// <summary>تصفير عدادات الاستهلاك من كارت الحالة</summary>
    private void ResetMeters_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetMeters();
        _vm.Log("🗑 صُفِّرت عدادات الاستهلاك.");
    }

    /// <summary>زر تصفير الإعدادات السريعة — القيم ترجع 100% والمعاينة تتحدّث لايف تلقائيًا</summary>
    private void ResetQuickAdjust_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetQuickAdjustments();
        _vm.Log("↺ أُعيدت الإعدادات السريعة إلى 100%.");
    }

    // ════════════════════════════════════════════════════════════════════
    //  المعاينة الحية — السلايدرز تُحدّث المعاينة فورًا
    // ════════════════════════════════════════════════════════════════════

    private void VmLivePreview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !LivePreviewTriggers.Contains(e.PropertyName)) return;
        if (_vm.IsBusy) return;
        if (string.IsNullOrEmpty(_vm.CurrentImagePath) || !File.Exists(_vm.CurrentImagePath)) return;

        _livePreviewTimer?.Stop();
        _livePreviewTimer?.Start();
    }

    /// <summary>إعادة تشغيل المعاينة بإعدادات اللحظة — مصغّرة للسرعة مع إلغاء أي تشغيل قديم</summary>
    private async Task RunLivePreviewAsync()
    {
        if (_vm.IsBusy) return;
        if (string.IsNullOrEmpty(_vm.CurrentImagePath) || !File.Exists(_vm.CurrentImagePath)) return;

        _liveCts?.Cancel();
        _liveCts = new CancellationTokenSource();
        var token = _liveCts.Token;
        int run = ++_liveRunId;

        try
        {
            var imagePath = _vm.CurrentImagePath;
            var settings  = _vm.BuildPipelineSettings();

            var halftone = await Task.Run(() =>
            {
                var pipeline = new RipPipeline(settings);
                return pipeline.PreviewHalftone(imagePath, previewMaxSide: 420);
            }, token);

            if (run != _liveRunId) return;   // جاء تحديث أحدث — تجاهل النتيجة القديمة

            _lastHalftone = halftone;
            UpdatePreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (run == _liveRunId) _vm.Log($"⚠ المعاينة الحية توقفت: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  🔌 تشخيص الاتصال (Stage 4)
    // ════════════════════════════════════════════════════════════════════

    private void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        _vm.Log("🔌 بدء تشخيص الاتصال بالطابعة…");

        Task.Run(() =>
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
                        PrinterStatusDot.Fill = (Brush)FindResource("SuccessGreenBrush");
                        RefreshPrinterStatusPanel();
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

            PreviewImage.Source = bmp;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            var fi = new FileInfo(path);
            PreviewInfoText.Text = $"{Path.GetFileName(path)} — {bmp.PixelWidth}×{bmp.PixelHeight}px — {fi.Length / 1024.0:F0} KB";
        }
        catch (Exception ex)
        {
            _vm.Log($"❌ تعذر تحميل المعاينة: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  المعاينة (بدون طباعة)
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

            var halftone = await Task.Run(() =>
            {
                var pipeline = new RipPipeline(settings);
                return pipeline.PreviewHalftone(imagePath);
            }, token);

            _vm.RaisePreviewReady(halftone);
            _vm.Log("✅ المعاينة جاهزة — اضغط مربع لون لعرض قناته منفردة.");
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
    //  تحديث المعاينة حسب القناة
    // ════════════════════════════════════════════════════════════════════

    private void UpdatePreview()
    {
        if (_lastHalftone is null) return;

        int idx = _previewChannelIndex;

        var small = HalftonePreviewRenderer.Render(_lastHalftone, idx);
        PreviewImage.Source = HalftonePreviewRenderer.Upscale(small, 320) ?? small;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        PreviewInfoText.Text = $"معاينة منقّطة {_lastHalftone.Width}×{_lastHalftone.Height}px — " +
                               (idx < 0 ? "توليفية (6 قنوات)"
                                        : new[]{"Cyan","Magenta","Yellow","Black","LightCyan","LightMagenta"}[idx]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  الطباعة — async
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

        _vm.PrintCommand.Execute(null);

        while (_vm.IsBusy)
            await Task.Delay(200);

        CancelPrintButton.IsEnabled = false;
        PrintButton.IsEnabled = true;

        if (!string.IsNullOrEmpty(_vm.CurrentImagePath))
            UpdateConsumptionEstimate(_vm.CurrentImagePath);
    }

    private void CancelPrint_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PrintCommand is AsyncPrintCommand cmd)
            cmd.Cancel();
        _vm.Log("🛑 طُلب الإلغاء…");
    }

    // ════════════════════════════════════════════════════════════════════
    //  وضع الورشة السريع ⚡ — الرئيسية فقط بدون تنقّل
    // ════════════════════════════════════════════════════════════════════

    private void QuickMode_Changed(object sender, RoutedEventArgs e)
    {
        if (NavBar is null || HomePanel is null || PageHost is null) return;

        bool quick = QuickModeToggle?.IsChecked == true;

        NavBar.Visibility = quick ? Visibility.Collapsed : Visibility.Visible;
        if (quick)
        {
            PageHost.Content = null;
            PageHost.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
        }

        _vm?.Log(quick
            ? "⚡ وضع الورشة السريع: الرئيسية فقط — كل ما تحتاجه في شاشة واحدة"
            : "🔧 الوضع الاحترافي الكامل: كل الصفحات متاحة");
    }

    // ════════════════════════════════════════════════════════════════════
    //  حالة الطابعة
    // ════════════════════════════════════════════════════════════════════

    private void UpdatePrinterStatus(bool initial = false)
    {
        Task.Run(() =>
        {
            DiscoveredPrinter[]? printers = null;
            try { printers = UsbPrinterPort.DiscoverL1800(); } catch { /* بدون LibUsb */ }

            Dispatcher.BeginInvoke(() =>
            {
                bool found = printers is { Length: > 0 };

                PrinterStatusDot.Fill = found
                    ? (Brush)FindResource("SuccessGreenBrush")
                    : (Brush)FindResource("TextMutedBrush");

                PrinterStatusText.Text = found
                    ? "الطابعة: متصلة ✓"
                    : "الطابعة: غير متصلة";

                if (found) _vm.Log($"🖨 اتصلت طابعة: {printers![0].Name}");
            });
        });
    }

    // ════════════════════════════════════════════════════════════════════
    //  تاب حالات الطابعة — يفتح ويقفل، وبالفتح يفحص الوضع الحالي
    // ════════════════════════════════════════════════════════════════════

    private void PrinterStatusExpander_Expanded(object sender, RoutedEventArgs e)
        => RefreshPrinterStatusPanel();

    /// <summary>تحديث لوح حالات الطابعة داخل التاب + مزامنة الشريط العلوي عند الاتصال</summary>
    private void RefreshPrinterStatusPanel()
    {
        Task.Run(() =>
        {
            DiscoveredPrinter[]? printers = null;
            try { printers = UsbPrinterPort.DiscoverL1800(); } catch { /* بدون LibUsb */ }

            Dispatcher.BeginInvoke(() =>
            {
                bool found = printers is { Length: > 0 };

                PanelStatusDot.Fill = found
                    ? (Brush)FindResource("SuccessGreenBrush")
                    : (Brush)FindResource("TextMutedBrush");

                PanelStatusText.Text  = found ? "الطابعة: متصلة ✓" : "الطابعة: غير متصلة";
                PanelPrinterName.Text = found ? printers![0].ToString() : "لم تُكتشف طابعة على USB";
                PanelTransport.Text   = found ? "LibUsb (WinUSB)" : "—";
                PanelLastCheck.Text   = $"آخر فحص {DateTime.Now:HH:mm:ss}";

                if (found)
                {
                    // مزامنة الشريط العلوي — لا ننزل الحالة لو سبق وأكد التشخيص اتصال
                    PrinterStatusDot.Fill = (Brush)FindResource("SuccessGreenBrush");
                    if (PrinterStatusText.Text == "الطابعة: غير متصلة")
                        PrinterStatusText.Text = "الطابعة: متصلة ✓";
                    _vm.Log($"🖨 اتصلت طابعة: {printers![0].Name}");
                }
            });
        });
    }
}
