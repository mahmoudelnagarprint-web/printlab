using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElnagarPrint.RIP.Core;
using ElnagarPrint.RIP.UI.Models;

namespace ElnagarPrint.RIP.UI.ViewModels;

/// <summary>
/// الـ ViewModel الرئيسي — يربط كل التبويبات السبعة بمحرك الـ Core
/// • يحمل كل إعدادات الواجهة (Properties مرتبطة Two-way)
/// • يشغّل pipeline الطباعة في خيط خلفي (async)
/// • يدير السجلات وشريط التقدير والمعاينة
/// </summary>
public class MainViewModel : ObservableBase
{
    // ════════════════════════════════════════════════════════════════════
    //  الإعدادات الكاملة — تبويب Main
    // ════════════════════════════════════════════════════════════════════

    private PaperSize _paperSize = PaperSize.A4;
    public PaperSize PaperSize { get => _paperSize; set => Set(ref _paperSize, value); }

    private PaperSourceKind _paperSource = PaperSourceKind.RearTray;
    public PaperSourceKind PaperSource { get => _paperSource; set => Set(ref _paperSource, value); }

    private bool _borderless = true;
    public bool Borderless { get => _borderless; set => Set(ref _borderless, value); }

    private int _qualityLevel = 5;   // 1..7 (مستوى الجودة)
    public int QualityLevel
    {
        get => _qualityLevel;
        set { if (Set(ref _qualityLevel, Math.Clamp(value, 1, 7)))
              OnPropertyChanged(nameof(QualityDescription)); }
    }
    public string QualityDescription => QualityLevel switch
    {
        1 => "مسودة سريعة جدًا",
        2 => "مسودة سريعة",
        3 => "معتاد اقتصادي",
        4 => "معتاد",
        5 => "عالي — موصى به للورشة",
        6 => "عالي جدًا",
        _ => "أقصى جودة (أبطأ)"
    };

    private double _brightness = 1.0;   // 0.0..2.0
    public double Brightness { get => _brightness; set => Set(ref _brightness, value); }

    private double _contrast = 1.0;     // 0.0..2.0
    public double Contrast { get => _contrast; set => Set(ref _contrast, value); }

    private double _density = 1.0;      // 0.0..2.0 (كثافة الحبر)
    public double Density { get => _density; set => Set(ref _density, value); }

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Layout
    // ════════════════════════════════════════════════════════════════════

    private OrientationKind _orientation = OrientationKind.Portrait;
    public OrientationKind Orientation { get => _orientation; set => Set(ref _orientation, value); }

    private DuplexMode _duplex = DuplexMode.OneSided;
    public DuplexMode Duplex { get => _duplex; set => Set(ref _duplex, value); }

    private int _copies = 1;
    public int Copies
    {
        get => _copies;
        set => Set(ref _copies, Math.Max(1, value));
    }

    private NUpMode _nUp = NUpMode.One;
    public NUpMode NUp { get => _nUp; set => Set(ref _nUp, value); }

    private bool _brochure = false;
    public bool Brochure { get => _brochure; set => Set(ref _brochure, value); }

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Color
    // ════════════════════════════════════════════════════════════════════

    private Models.ColorMode _colorMode = Models.ColorMode.VividPhoto;
    public Models.ColorMode ColorMode { get => _colorMode; set => Set(ref _colorMode, value); }

    private Models.RenderingIntent _renderingIntent = Models.RenderingIntent.Perception;
    public Models.RenderingIntent RenderingIntent { get => _renderingIntent; set => Set(ref _renderingIntent, value); }

    private ColorSpaceKind _colorSpace = ColorSpaceKind.sRGB;
    public ColorSpaceKind ColorSpace { get => _colorSpace; set => Set(ref _colorSpace, value); }

    private WhitePoint _whitePoint = WhitePoint.D65;
    public WhitePoint WhitePoint { get => _whitePoint; set => Set(ref _whitePoint, value); }

    private double _labL = 0;    // -100..100
    public double LabL { get => _labL; set => Set(ref _labL, value); }

    private double _labA = 0;
    public double LabA { get => _labA; set => Set(ref _labA, value); }

    private double _labB = 0;
    public double LabB { get => _labB; set => Set(ref _labB, value); }

    private double _saturation = 1.0;    // 0.0..2.0
    public double Saturation { get => _saturation; set => Set(ref _saturation, value); }

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Correction — تصحيح يدوي 0..200%
    // ════════════════════════════════════════════════════════════════════

    private double _corrCyan = 100;
    public double CorrCyan { get => _corrCyan; set => Set(ref _corrCyan, value); }

    private double _corrMagenta = 100;
    public double CorrMagenta { get => _corrMagenta; set => Set(ref _corrMagenta, value); }

    private double _corrYellow = 100;
    public double CorrYellow { get => _corrYellow; set => Set(ref _corrYellow, value); }

    private double _corrBlack = 100;
    public double CorrBlack { get => _corrBlack; set => Set(ref _corrBlack, value); }

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Consumption
    // ════════════════════════════════════════════════════════════════════

    private InkEstimate? _lastCost;
    public InkEstimate? LastCost
    {
        get => _lastCost;
        set
        {
            if (Set(ref _lastCost, value))
            {
                OnPropertyChanged(nameof(CostTotalMl));
                OnPropertyChanged(nameof(CostTotalEgp));
                OnPropertyChanged(nameof(CostPerCopyEgp));
                OnPropertyChanged(nameof(CostBreakdown));
            }
        }
    }

    public string CostTotalMl   => LastCost is null ? "—" : $"{LastCost.TotalMilliliters:F2} مل";
    public string CostTotalEgp  => LastCost is null ? "—" : $"{LastCost.TotalCostEGP:F2} ج.م";
    public string CostPerCopyEgp => LastCost is null
        ? "—"
        : $"{LastCost.TotalCostEGP:F2} ج.م/نسخة";
    public string CostBreakdown => LastCost is null
        ? "—"
        : $"C {LastCost.MillilitersC:F1} | M {LastCost.MillilitersM:F1} | Y {LastCost.MillilitersY:F1} | K {LastCost.MillilitersK:F1} | Lc {LastCost.MillilitersLc:F1} | Lm {LastCost.MillilitersLm:F1} مل";

    /// <summary>زر تصفير العدادات</summary>
    public void ResetMeters() => LastCost = null;

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Ink Ctrl
    // ════════════════════════════════════════════════════════════════════

    private double _blackLevel = 0;      // UCR 0..100
    public double BlackLevel { get => _blackLevel; set => Set(ref _blackLevel, value); }

    private double _totalInkLimit = 300; // 100..400 %
    public double TotalInkLimit
    {
        get => _totalInkLimit;
        set => Set(ref _totalInkLimit, Math.Clamp(value, 100, 400));
    }

    private double _cyanInkLimit = 100;
    public double CyanInkLimit { get => _cyanInkLimit; set => Set(ref _cyanInkLimit, value); }

    private double _magentaInkLimit = 100;
    public double MagentaInkLimit { get => _magentaInkLimit; set => Set(ref _magentaInkLimit, value); }

    private double _yellowInkLimit = 100;
    public double YellowInkLimit { get => _yellowInkLimit; set => Set(ref _yellowInkLimit, value); }

    private double _blackInkLimit = 100;
    public double BlackInkLimit { get => _blackInkLimit; set => Set(ref _blackInkLimit, value); }

    // ════════════════════════════════════════════════════════════════════
    //  تبويب Options
    // ════════════════════════════════════════════════════════════════════

    private BitDepth _bitDepth = BitDepth.Eight;
    public BitDepth BitDepth { get => _bitDepth; set => Set(ref _bitDepth, value); }

    private bool _autoRollCut = false;
    public bool AutoRollCut { get => _autoRollCut; set => Set(ref _autoRollCut, value); }

    private bool _dryingPause = true;
    public bool DryingPause { get => _dryingPause; set => Set(ref _dryingPause, value); }

    private int _dryingPauseMs = 600;
    public int DryingPauseMs { get => _dryingPauseMs; set => Set(ref _dryingPauseMs, value); }

    private DirectionMode _printDirection = DirectionMode.BiDirectional;
    public DirectionMode PrintDirection { get => _printDirection; set => Set(ref _printDirection, value); }

    private HeadHeightLevel _headHeight = HeadHeightLevel.Level2;
    public HeadHeightLevel HeadHeight
    {
        get => _headHeight;
        set
        {
            if (Set(ref _headHeight, value))
                OnPropertyChanged(nameof(HeadHeightValue));
        }
    }

    /// <summary>proxy عددي للـ Slider — 0..5</summary>
    public double HeadHeightValue
    {
        get => (double)HeadHeight;
        set => HeadHeight = (HeadHeightLevel)(int)Math.Clamp(value, 0, 5);
    }

    private PrintResolutionKind _resolution = PrintResolutionKind.Dpi720;
    public PrintResolutionKind Resolution { get => _resolution; set => Set(ref _resolution, value); }

    private bool _useRleCompression = true;
    public bool UseRleCompression { get => _useRleCompression; set => Set(ref _useRleCompression, value); }

    private bool _useWeave = true;
    public bool UseWeave { get => _useWeave; set => Set(ref _useWeave, value); }

    /// <summary>زر Zero Lm/Lc السريع ⚡ — قفل فعلي للقناتين (0 حبر)</summary>
    public void ZeroLightInks()
    {
        LockLightCyan = true;
        LockLightMagenta = true;
    }

    /// <summary>إرجاع الألوان الفاتحة للعمل — فك القفل و 100%</summary>
    public void RestoreLightInks()
    {
        LockLightCyan = false;
        LockLightMagenta = false;
        LcInkPercent = 100;
        LmInkPercent = 100;
    }

    // ════════════════════════════════════════════════════════════════════
    //  الألوان الفاتحة — قفل + نسبة لكل قناة (حقيقي: يصل للـ pipeline)
    // ════════════════════════════════════════════════════════════════════

    private bool _lockLightCyan = false;
    /// <summary>قفل Light Cyan — عند التفعيل القناة تُطفأ كليًا (gate=0) بغض النظر عن النسبة</summary>
    public bool LockLightCyan
    {
        get => _lockLightCyan;
        set { if (Set(ref _lockLightCyan, value))
              { OnPropertyChanged(nameof(EffectiveLightCyanGate));
                OnPropertyChanged(nameof(EffectiveLightMagentaGate)); } }
    }

    private bool _lockLightMagenta = true;   // Lm مطفأ افتراضيًا (ورشة الفيزيل/السبلمايشن)
    /// <summary>قفل Light Magenta — إطفاء كلي</summary>
    public bool LockLightMagenta
    {
        get => _lockLightMagenta;
        set { if (Set(ref _lockLightMagenta, value))
              { OnPropertyChanged(nameof(EffectiveLightCyanGate));
                OnPropertyChanged(nameof(EffectiveLightMagentaGate)); } }
    }

    private double _lcInkPercent = 100;      // 0..100
    public double LcInkPercent
    {
        get => _lcInkPercent;
        set { if (Set(ref _lcInkPercent, value))
                OnPropertyChanged(nameof(EffectiveLightCyanGate)); }
    }

    private double _lmInkPercent = 100;      // 0..100
    public double LmInkPercent
    {
        get => _lmInkPercent;
        set { if (Set(ref _lmInkPercent, value))
                OnPropertyChanged(nameof(EffectiveLightMagentaGate)); }
    }

    /// <summary>الـ Gate الفعلي لـ Light Cyan — المُمرَّر للمحرك (0..1)</summary>
    public double EffectiveLightCyanGate => LockLightCyan ? 0.0 : Math.Clamp(LcInkPercent, 0, 100) / 100.0;

    /// <summary>الـ Gate الفعلي لـ Light Magenta — المُمرَّر للمحرك (0..1)</summary>
    public double EffectiveLightMagentaGate => LockLightMagenta ? 0.0 : Math.Clamp(LmInkPercent, 0, 100) / 100.0;

    // ════════════════════════════════════════════════════════════════════
    //  الحالة الجارية: السجلات + التقدير + الصورة الحالية
    // ════════════════════════════════════════════════════════════════════

    public ObservableCollection<string> Logs { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPrint));
                PrintCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanPrint => !IsBusy && !string.IsNullOrEmpty(CurrentImagePath) && File.Exists(CurrentImagePath);

    private string? _currentImagePath;
    public string? CurrentImagePath
    {
        get => _currentImagePath;
        set
        {
            if (Set(ref _currentImagePath, value))
            {
                OnPropertyChanged(nameof(CurrentImageName));
                PrintCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CurrentImageName => string.IsNullOrEmpty(CurrentImagePath)
        ? "لم يُحدد ملف"
        : Path.GetFileName(CurrentImagePath);

    private int _progressPercent;
    public int ProgressPercent { get => _progressPercent; set => Set(ref _progressPercent, value); }

    private string _progressText = "جاهز — اختر صورة واضغط طباعة";
    public string ProgressText { get => _progressText; set => Set(ref _progressText, value); }

    /// <summary>أمر الطباعة (async)</summary>
    public AsyncPrintCommand PrintCommand { get; }

    /// <summary>حدث يسأله الـ View عن إضافة سطر للسجل</summary>
    public event EventHandler<string>? LogAdded;

    public event EventHandler<HalftoneResult>? PreviewReady;

    public MainViewModel()
    {
        PrintCommand = new AsyncPrintCommand(this);
        Log("✨ أهلاً بك في نظام النجار برنت RIP — Epson L1800");
        Log("حدد صورة ثم اضغط «طباعة» أو «معاينة» لرؤية نتيجة التنقيط.");
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}]  {message}";
        Logs.Add(line);
        LogAdded?.Invoke(this, line);
    }

    /// <summary>ارفع نتيجة المعاينة للـ View</summary>
    public void RaisePreviewReady(HalftoneResult result) => PreviewReady?.Invoke(this, result);

    // ════════════════════════════════════════════════════════════════════
    //  BuildPipelineSettings — تجميع إعدادات الـ UI في PipelineSettings
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔗 نقطة الربط الوحيدة بين الواجهة والمحرك:
    /// تحوّل كل خصائص التبويبات السبعة إلى PipelineSettings للمحرك
    /// </summary>
    public PipelineSettings BuildPipelineSettings()
    {
        var s = new PipelineSettings
        {
            // Gates — الإطفاء فقط (Zero Lm/Lc)؛ التصحيح 0-200% يمشي عبر Corr*Percent
            // (Stage 4: PipelineSettings.Corr*Percent → InkControlSettings.Correction*)
            CyanGate           = 1.0,
            MagentaGate        = 1.0,
            YellowGate         = 1.0,
            BlackGate          = 1.0,
            // الألوان الفاتحة — القفل/النسبة الفعلية من تبويب Ink Ctrl
            LightCyanGate      = EffectiveLightCyanGate,
            LightMagentaGate   = EffectiveLightMagentaGate,

            // Halftone
            UseMultiLevel      = true,
            LightThreshold     = 0.45,
            DarkThreshold      = 0.50,
            YellowThreshold    = 0.48,

            // ESC/P2
            Resolution         = Resolution switch
            {
                PrintResolutionKind.Dpi360  => Core.PrintResolution.Dpi360,
                PrintResolutionKind.Dpi720  => Core.PrintResolution.Dpi720,
                PrintResolutionKind.Dpi1440 => Core.PrintResolution.Dpi1440,
                _                           => Core.PrintResolution.Dpi2880
            },
            InkSet             = Core.InkSet.CMYKcc,
            UseCompression     = UseRleCompression,
            UseMicroWeave      = UseWeave,
            BiDirectional      = PrintDirection == DirectionMode.BiDirectional,

            // USB / mechanics
            InterPassDelayMs   = DryingPause ? DryingPauseMs : 0,
            UseMultiPass       = true,

            // ── تبويب Main ──
            PaperSizeIndex     = (int)PaperSize,
            PaperSourceIndex   = (int)PaperSource,
            Borderless         = Borderless,
            QualityLevel       = QualityLevel,

            // ── ضبط الصورة ──
            Brightness         = Brightness,
            Contrast           = Contrast,
            Density            = Density,

            // ── تبويب Layout ──
            OrientationIndex   = (int)Orientation,
            DuplexIndex        = (int)Duplex,
            Copies             = Copies,
            NUpPages           = (int)NUp,
            Brochure           = Brochure,

            // ── تبويب Color ──
            ColorModeIndex       = (int)ColorMode,
            RenderingIntentIndex = (int)RenderingIntent,
            ColorSpaceIndex      = (int)ColorSpace,
            WhitePointIndex      = (int)WhitePoint,
            LabL               = LabL,
            LabA               = LabA,
            LabB               = LabB,
            Saturation         = Saturation,

            // ── تبويب Correction ──
            CorrCyanPercent    = CorrCyan,
            CorrMagentaPercent = CorrMagenta,
            CorrYellowPercent  = CorrYellow,
            CorrBlackPercent   = CorrBlack,

            // ── تبويب Ink Ctrl ──
            BlackLevelPercent     = BlackLevel,
            TotalInkLimitPercent  = TotalInkLimit,
            CyanInkLimitPercent   = CyanInkLimit,
            MagentaInkLimitPercent= MagentaInkLimit,
            YellowInkLimitPercent = YellowInkLimit,
            BlackInkLimitPercent  = BlackInkLimit,

            // ── تبويب Options ──
            BitDepth           = (int)BitDepth,
            AutoRollCut        = AutoRollCut,
            SmartDryingPause   = DryingPause,
            HeadHeightLevel    = (int)HeadHeight,
            UseWeave           = UseWeave
        };
        return s;
    }
}