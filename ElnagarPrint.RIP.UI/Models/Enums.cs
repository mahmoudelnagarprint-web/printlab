namespace ElnagarPrint.RIP.UI.Models;

// ══════════════════════════════════════════════════════════════════════════
//  أنواع القيم المنطقية (Enums) — تُخدم تبويبات الواجهة السبعة
// ══════════════════════════════════════════════════════════════════════════

/// <summary>تبويب Main — مقاسات الورق المدعومة على L1800</summary>
public enum PaperSize
{
    A4,
    A5,
    A3,
    A3Plus,      // A3+ (329×483mm) — الشائع للطباعة الفنية
    Letter,
    Legal,
    Roll100mm,   // رول 100mm
    Roll210mm,   // رول 210mm
    Custom       // مخصص
}

/// <summary>تبويب Main — مصدر الورق</summary>
public enum PaperSourceKind
{
    RearTray,    // الدرج الخلفي (الأفضل للأفلام والتصويم)
    Cassette,    // الكاسيت الأمامي
    RollPaper,   // ورق رول
    Manual       // يدوي
}

/// <summary>تبويب Layout — الاتجاه</summary>
public enum OrientationKind
{
    Portrait,    // عمودي
    Landscape    // أفقي
}

/// <summary>تبويب Layout — الطباعة على الوجهين</summary>
public enum DuplexMode
{
    OneSided,       // وجه واحد
    TwoSidedLong,   // وجهان — ربط الحافة الطويلة
    TwoSidedShort   // وجهان — ربط الحافة القصيرة
}

/// <summary>تبويب Layout — تخطيط N-up (عدد الصفحات في ورقة)</summary>
public enum NUpMode
{
    One,    // 1 صفحة
    Two,    // 2 صفحة
    Four,   // 4 صفحات
    Nine,   // 9 صفحات
    Sixteen // 16 صفحة
}

/// <summary>تبويب Color — أوضاع اللون الستة (من REA §6)</summary>
public enum ColorMode
{
    Monochrome      = 0,   // رمادي — K فقط
    GrayscaleRGB    = 1,   // رمادي بثلاث قنوات
    StandardPhoto   = 2,   // صورة قياسية CMYK
    VividPhoto      = 3,   // صورة زاهية (تصلح للفيزيل)
    Sublimation     = 4,   // سبلمايشن — حبر كثيف
    FilmTransparent = 5    // فيلم شفاف — للصناعي/المطبوعات
}

/// <summary>تبويب Color — نوايا تحويل ICC (من REA §6.2 — IccColorEngine)</summary>
public enum RenderingIntent
{
    PerceptionInksave = 0,  // توفير حبر 20-35% (Chroma × 0.72)
    PerceptionPhoto   = 1,  // إدراكي للصور
    Perception        = 2,  // إدراكي قياسي
    Saturation        = 3,  // تشبع — للجرافيك
    ColorimetricAbs   = 4,  // لوني مطلق
    ColorimetricRel   = 5,  // لوني نسبي
    NoCorrection      = 6   // بدون تصحيح (bypass)
}

/// <summary>تبويب Color — فضاءات الألوان المدعومة</summary>
public enum ColorSpaceKind
{
    sRGB,
    AdobeRGB,
    ProPhotoRGB,
    DeviceCMYK
}

/// <summary>تبويب Color — نقطة البياض</summary>
public enum WhitePoint
{
    D50,   // الطباعة الفنية
    D65    // الشاشات والصور
}

/// <summary>تبويب Options — عمق المعالجة</summary>
public enum BitDepth
{
    Eight   = 8,
    Sixteen = 16
}

/// <summary>تبويب Options — اتجاه الطباعة</summary>
public enum DirectionMode
{
    UniDirectional,  // اتجاه واحد — جودة أعلى
    BiDirectional    // اتجاهان — سرعة أعلى
}

/// <summary>تبويب Options — ارتفاع الرأس</summary>
public enum HeadHeightLevel
{
    Level0 = 0,   // أقرب — للأوراق الناعمة
    Level1 = 1,
    Level2 = 2,   // القياسي
    Level3 = 3,
    Level4 = 4,
    Level5 = 5    // الأبعد — للأوراق السميكة
}

/// <summary>تبويب Options — دقة الطباعة</summary>
public enum PrintResolutionKind
{
    Dpi360,
    Dpi720,
    Dpi1440,
    Dpi2880
}

/// <summary>وضع الورشة السريع — Quick Workshop Mode</summary>
public enum WorkshopMode
{
    FullPro,     // كامل التبويبات السبعة
    QuickDaily   // مبسط للتشغيل اليومي
}