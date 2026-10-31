// MechanicsControl.cs — مكونات الميكانيكا والتحكم في التجفيف والاتجاه
// Stage 2 — ElnagarPrint.RIP.Core
// Target: Epson L1800

namespace ElnagarPrint.RIP.Core;

/// <summary>
/// إعدادات drying pause — وقفة التجفيف بين التمريرات
/// </summary>
public sealed class InkDryingPauseSettings
{
    /// <summary>الوقفه الافتراضية بالملي ثانية</summary>
    public int DefaultPauseMs { get; init; } = 150;

    /// <summary>عتبة الكثافة التي تفعّل الوقفة الإضافية (0.0–1.0)</summary>
    public double TriggerThreshold { get; init; } = 0.70;

    /// <summary>معامل التضخيم للكثافة العالية</summary>
    public double DensityBoostFactor { get; init; } = 1.5;

    /// <summary>الحد الأقصى للوقفه بالملي ثانية</summary>
    public int MaxPauseMs { get; init; } = 800;
}

/// <summary>
/// إعدادات اتجاه الطباعة
/// </summary>
public enum PrintDirection
{
    UniDirectional = 0, // طباعة في اتجاه واحد
    BiDirectional  = 1   // طباعة ذهاباً وإياباً
}

/// <summary>
/// ارتفاع الرأس — يتحكم في المسافة بين الرأس والورقة
/// </summary>
public enum HeadHeight
{
    Height0 = 0, // الأقرب
    Height1 = 1,
    Height2 = 2,
    Height3 = 3,
    Height4 = 4,
    Height5 = 5  // الأبعد
}

/// <summary>
/// إعدادات القطع (Roll Cutter)
/// </summary>
public sealed class RollCutterSettings
{
    public bool  Enabled          { get; init; } = false;
    public bool  AutoCutAtEnd      { get; init; } = true;
    public int   CutIntervalCm    { get; init; } = 30; // قطع كل N سم
    public int   CutDelayMs       { get; init; } = 200;
}

/// <summary>
/// إعدادات الورق والتغذية
/// </summary>
public sealed class PaperFeedSettings
{
    /// <summary>العرض الفعّال للرأس بالبكسل</summary>
    public int    PrintableWidthPx { get; init; } = 900;

    /// <summary>نوع الورق — يؤثر على سرعة التجفيف</summary>
    public string PaperType        { get; init; } = "vinyl"; // vinyl, sublimation, film, plain

    /// <summary>تفعيل التغذية المحسّنة للورق الرقيق</summary>
    public bool   ThinPaperMode   { get; init; } = false;

    /// <summary>تفعيل التحقق من نفاد الورق</summary>
    public bool   OutOfPaperSensor { get; init; } = true;
}

/// <summary>
/// الملف الكامل للميكانيكا — يجمع كل الإعدادات
/// </summary>
public sealed class MechanicsSettings
{
    public PrintDirection  Direction        { get; init; } = PrintDirection.BiDirectional;
    public HeadHeight      HeadHeightLevel  { get; init; } = HeadHeight.Height2;
    public RollCutterSettings RollCutter     { get; init; } = new();
    public PaperFeedSettings PaperFeed      { get; init; } = new();
    public InkDryingPauseSettings DryingPause { get; init; } = new();
}

/// <summary>
/// نتيجة حساب خطوة التجفيف — إرجاع مدة الوقفة المقترحة
/// </summary>
public sealed record DryingPauseResult(
    int    PauseMs,
    double RowDensity,
    bool   Triggered
);

/// <summary>
/// محرّك حساب وقت التجفيف الذكي — يعتمد على كثافة الحبر في كل خط
/// </summary>
public static class SmartDryingPauseTrigger
{
    /// <summary>
    /// حساب مدة الوقفة لصف واحد بناءً على كثافته
    /// </summary>
    /// <param name="rowDotLevels">مصفوفة مستويات النقاط للصف (6 ألوان × عدد البكسلات)</param>
    /// <param name="basePauseMs">الوقفه الأساسية</param>
    /// <param name="settings">إعدادات التجفيف</param>
    public static DryingPauseResult ComputePauseMs(
        double[,] rowDotLevels,
        int       basePauseMs,
        InkDryingPauseSettings? settings = null)
    {
        settings ??= new InkDryingPauseSettings();

        double density = ComputeRowDensity(rowDotLevels);

        if (density < settings.TriggerThreshold)
            return new DryingPauseResult(0, density, false);

        // تضخيم الوقفة حسب الكثافة
        double multiplier = 1.0 + (density - settings.TriggerThreshold)
                                     * settings.DensityBoostFactor;

        int pauseMs = (int)Math.Round(basePauseMs * multiplier);
        pauseMs = Math.Min(pauseMs, settings.MaxPauseMs);

        return new DryingPauseResult(pauseMs, density, true);
    }

    /// <summary>
    /// كثافة الصف = متوسط كل النقاط مقسوماً على الحد الأقصى (3 لـ 2-bit)
    /// </summary>
    public static double ComputeRowDensity(double[,] rowDotLevels)
    {
        if (rowDotLevels.Length == 0) return 0.0;

        double sum = 0.0;
        foreach (double v in rowDotLevels)
            sum += v;

        // مستويان: 0 و 3 (min/max) — نريد نسبة ملء الصفحة
        return sum / rowDotLevels.Length / 3.0;
    }
}

/// <summary>
/// معالج التجفيف — يحسب وقفات ثابتة للصفوف كلها
/// </summary>
public static class DryingPauseCalculator
{
    public static int[] ComputeAllPauseMs(
        HalftoneResult halftoneResult,
        int basePauseMs,
        InkDryingPauseSettings? settings = null)
    {
        int rowCount = halftoneResult.Height;
        int[] pauses = new int[rowCount];

        for (int y = 0; y < rowCount; y++)
        {
            double[,] row = ExtractRowLevels(halftoneResult, y);
            var result = SmartDryingPauseTrigger.ComputePauseMs(row, basePauseMs, settings);
            pauses[y] = result.PauseMs;
        }

        return pauses;
    }

    private static double[,] ExtractRowLevels(HalftoneResult halftone, int row)
    {
        // 6 channels × width — DotMap layout: [row * Width + col]
        int width = halftone.Width;
        var levels = new double[6, width];

        for (int ch = 0; ch < 6; ch++)
        {
            var channel = halftone.Channels[ch];
            if (channel == null || channel.DotMap == null) continue;

            int rowOffset = row * width;
            for (int x = 0; x < width; x++)
            {
                // DotMap holds 0/1/2/3 — convert to level
                levels[ch, x] = channel.DotMap[rowOffset + x];
            }
        }

        return levels;
    }
}

/// <summary>
/// معالج أوامر القطع للرول
/// </summary>
public static class RollCutterController
{
    /// <summary>بناء أمر قطع Raster Line skip count للـ ESC/P2</summary>
    public static byte[] BuildCutCommand(RollCutterSettings settings)
    {
        // GS V (cut command) — نستخدم الأمر المبسط
        // ESC p — pulse signal للإطلاق
        if (!settings.Enabled) return [];

        // أمر القطع: GS V m
        // m=1 cut and feed, m=66 auto-cut
        byte mode = settings.AutoCutAtEnd ? (byte)66 : (byte)1;
        return [0x1D, 0x56, mode];
    }

    /// <summary>حساب عدد السطور التي تمثل N سم على الورق</summary>
    public static int CmToRasterLines(double cm, int resolutionDpi)
    {
        // 1 inch = 2.54 cm, dpi lines per cm
        return (int)Math.Round(resolutionDpi * cm / 2.54);
    }
}

/// <summary>
/// المعالج الرئيسي للميكانيكا — يجمع الإعدادات ويربطها بـ ESC/P2
/// </summary>
public sealed class MechanicsControlProcessor
{
    public MechanicsSettings Settings { get; }

    public MechanicsControlProcessor(MechanicsSettings? settings = null)
    {
        Settings = settings ?? new MechanicsSettings();
    }

    /// <summary>بناء أمر تهيئة الاتجاه (Uni/Bi) — GS ( W</summary>
    public byte[] BuildDirectionCommand()
    {
        // Print direction in ESC/P2
        // GS ( W 02 00 00 01 → bidirectional (0) / unidirectional (1)
        byte dir = Settings.Direction == PrintDirection.UniDirectional ? (byte)1 : (byte)0;
        return [0x1D, 0x28, 0x57, 0x02, 0x00, 0x00, 0x01, dir];
    }

    /// <summary>بناء أمر ضبط ارتفاع الرأس — ESC Space (head gap)</summary>
    public byte[] BuildHeadHeightCommand()
    {
        // ESC SP n — set inter-character gap (also controls head height on L1800)
        // Height0 = n×16, Height5 = n×64 approx
        byte n = (byte)((int)Settings.HeadHeightLevel * 10 + 16);
        return [0x1B, 0x20, n];
    }

    /// <summary>بناء أمر تهيئة الورق والتغذية</summary>
    public byte[] BuildPaperFeedCommand()
    {
        if (Settings.PaperFeed.ThinPaperMode)
        {
            // وضع الورق الرقيق — سرعة أقل
            // ESC EM 01 (eject) — not used here
            // Instead: custom feed speed via vendor extension
            return [0x1B, 0x25, 0x4C, 0x01]; // vendor: slow feed
        }
        return [];
    }

    /// <summary>الفاصل الزمني بين كل تمريرة (inter-pass) بالملي ثانية</summary>
    public int InterPassDelayMs(int passIndex, double density)
    {
        if (passIndex == 0) return 0; // لا وقفة قبل التمريرة الأولى

        var rowLevels = new double[1, 1];
        rowLevels[0, 0] = density;
        var pause = SmartDryingPauseTrigger.ComputePauseMs(
            rowLevels,
            Settings.DryingPause.DefaultPauseMs,
            Settings.DryingPause);

        return pause.PauseMs;
    }

    /// <summary>هل يعمل القطع التلقائي؟</summary>
    public bool AutoCutEnabled => Settings.RollCutter.Enabled && Settings.RollCutter.AutoCutAtEnd;

    /// <summary>إرجاع الملف الكامل كـ MechanicProfile</summary>
    public MechanicProfile BuildProfile()
    {
        return new MechanicProfile
        {
            DirectionCommand     = BuildDirectionCommand(),
            HeadHeightCommand    = BuildHeadHeightCommand(),
            PaperFeedCommand     = BuildPaperFeedCommand(),
            CutterCommand        = RollCutterController.BuildCutCommand(Settings.RollCutter),
            InterPassDelayMs    = Settings.DryingPause.DefaultPauseMs,
            MaxInterPassDelayMs = Settings.DryingPause.MaxPauseMs,
            PrintDirection       = Settings.Direction,
            HeadHeight           = Settings.HeadHeightLevel,
            PaperType            = Settings.PaperFeed.PaperType,
            ThinPaperMode        = Settings.PaperFeed.ThinPaperMode,
            PrintableWidthPx    = Settings.PaperFeed.PrintableWidthPx
        };
    }
}

/// <summary>
/// ملف الميكانيكا الكامل — يُمرَّر لـ ESC/P2 Encoder
/// </summary>
public sealed record MechanicProfile
{
    public byte[]         DirectionCommand      { get; init; } = [];
    public byte[]         HeadHeightCommand      { get; init; } = [];
    public byte[]         PaperFeedCommand       { get; init; } = [];
    public byte[]         CutterCommand          { get; init; } = [];
    public int            InterPassDelayMs       { get; init; }
    public int            MaxInterPassDelayMs    { get; init; }
    public PrintDirection PrintDirection         { get; init; }
    public HeadHeight     HeadHeight             { get; init; }
    public string         PaperType              { get; init; } = "vinyl";
    public bool           ThinPaperMode          { get; init; }
    public int            PrintableWidthPx       { get; init; } = 900;
}
