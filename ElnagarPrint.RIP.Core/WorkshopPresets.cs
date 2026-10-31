using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  WorkshopPresets.cs — نظام presets الخامات (JSON)
    //  Phase 1: Core Engine v2
    // ══════════════════════════════════════════════════════════════════════════

    #region ─── Preset Model ───────────────────────────────────────────────────

    /// <summary>
    /// إعدادات preset جاهز لعملية طباعة معينة
    /// </summary>
    public sealed class WorkshopPreset
    {
        /// <summary>اسم العرض (يظهر في القائمة)</summary>
        public string Name { get; set; } = "";

        /// <summary>وصف preset</summary>
        public string Description { get; set; } = "";

        /// <summary>الفئة: Sublimation / Vinyl / Canvas / Film / Cardstock / Plain</summary>
        public string Category { get; set; } = "";

        /// <summary>اسم ملف .tpp المطلوب (مثل "Epson_L1300.tpp")</summary>
        public string PrinterDefinition { get; set; } = "";

        /// <summary>اسم ملف .tpm (مثل "DX4250.tpm")</summary>
        public string ColorTable { get; set; } = "";

        // Ink Control
        public double BlackLevel { get; set; } = 100.0;
        public double BrightCyan { get; set; } = 50.0;
        public double BrightMagenta { get; set; } = 50.0;
        public double TotalInkMaximum { get; set; } = 300.0;

        // Channel limits
        public double ChannelLimitC  { get; set; } = 200.0;
        public double ChannelLimitM  { get; set; } = 200.0;
        public double ChannelLimitY  { get; set; } = 200.0;
        public double ChannelLimitK  { get; set; } = 200.0;

        // Gate sliders
        public double GateC  { get; set; } = 1.0;
        public double GateM  { get; set; } = 1.0;
        public double GateY  { get; set; } = 1.0;
        public double GateK  { get; set; } = 1.0;
        public double GateLc { get; set; } = 1.0;
        public double GateLm { get; set; } = 1.0;   // ⭐ تصفير Lm للـ Film

        // Color settings
        public string ColorMode { get; set; } = "ColorRGB";
        public string RenderingIntent { get; set; } = "Perception";
        public string ColorSpace { get; set; } = "sRGB";
        public bool BPC { get; set; } = true;

        // Adjustment sliders
        public double Brightness { get; set; } = 1.0;
        public double Contrast { get; set; } = 1.0;
        public double Saturation { get; set; } = 1.0;

        // LAB sliders
        public double LabL { get; set; } = 1.0;
        public double LabA { get; set; } = 0.0;
        public double LabB { get; set; } = 0.0;

        // Ink Control advanced
        public double ChannelLimitLc { get; set; } = 200.0;
        public double ChannelLimitLm { get; set; } = 200.0;
        public double BlackInkLimit { get; set; } = 100.0;

        // Per-channel correction
        public bool UseIndividualCorrection { get; set; } = false;
        public double CorrectionBlack   { get; set; } = 100.0;
        public double CorrectionCyan    { get; set; } = 100.0;
        public double CorrectionMagenta { get; set; } = 100.0;
        public double CorrectionYellow  { get; set; } = 100.0;

        // Smart Lm Compensation
        public bool SmartLmEnabled { get; set; } = false;
        public double SmartLmRatio { get; set; } = 0.35;

        // Halftone
        public string HalftoneMode { get; set; } = "FloydSteinberg";
        public int Bits { get; set; } = 2;

        // Quality preset index
        public int QualityIndex { get; set; } = 3;  // Normal quality

        /// <summary>ملاحظة للورشة</summary>
        public string Notes { get; set; } = "";

        /// <summary>اسم ملف ICC (إن وُجد)</summary>
        public string? IccProfile { get; set; }
    }

    /// <summary>
    /// ملف presets كامل — مجموعة من presets
    /// </summary>
    public sealed class WorkshopPresetLibrary
    {
        public string Version { get; set; } = "2.0";
        public string LastModified { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public WorkshopPreset[] Presets { get; set; } = Array.Empty<WorkshopPreset>();
    }

    #endregion

    #region ─── Manager ────────────────────────────────────────────────────────

    /// <summary>
    /// مدير presets — حفظ/استرجاع/بحث
    /// </summary>
    public static class WorkshopPresets
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// إنشاء مكتبة presets جاهزة لـ L1800
        /// </summary>
        public static WorkshopPresetLibrary CreateDefaultLibrary()
        {
            return new WorkshopPresetLibrary
            {
                Version = "2.0",
                LastModified = DateTime.Now.ToString("yyyy-MM-dd"),
                Presets = new[]
                {
                    // ── sublimation ──────────────────────
                    new WorkshopPreset
                    {
                        Name = "سبلميشن — ورق عادي",
                        Category = "Sublimation",
                        Description = "نقل حراري على التيشيرتات — ألوان صافية، تباين عالي",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 80,
                        BrightCyan = 60,
                        BrightMagenta = 60,
                        TotalInkMaximum = 280,
                        GateLm = 1.0,
                        RenderingIntent = "PerceptionPhoto",
                        IccProfile = "ISOcoated_v2_eci.icc",
                        QualityIndex = 3,
                        Notes = "أفضل تباين للطباعة على القماش الداكن"
                    },
                    new WorkshopPreset
                    {
                        Name = "سبلميشن — فينيل شفاف",
                        Category = "Sublimation",
                        Description = "للطباعة على فينيل شفاف — حبر أقل، ألوان زاهية",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 60,
                        BrightCyan = 70,
                        BrightMagenta = 70,
                        TotalInkMaximum = 260,
                        GateLm = 0.0,   // ⭐ تصفير Lm للشفافية
                        SmartLmEnabled = true,
                        SmartLmRatio = 0.3,
                        RenderingIntent = "Perception",
                        QualityIndex = 3,
                        Notes = "فينيل شفاف — تصفير Lm يعطي صافية ممتازة"
                    },

                    // ── vinyl / canvas ───────────────────
                    new WorkshopPreset
                    {
                        Name = "فينيل — عادي",
                        Category = "Vinyl",
                        Description = "طباعة على فينيل عادي (أبيض) — ألوان ممتلئة",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 100,
                        BrightCyan = 50,
                        BrightMagenta = 50,
                        TotalInkMaximum = 300,
                        GateLm = 1.0,
                        RenderingIntent = "Perception",
                        QualityIndex = 3
                    },
                    new WorkshopPreset
                    {
                        Name = "فينيل — ملون",
                        Category = "Vinyl",
                        Description = "فينيل ملون أو داكن — تباين أعلى، حبر أكثر",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 120,
                        BrightCyan = 50,
                        BrightMagenta = 50,
                        TotalInkMaximum = 350,
                        GateLm = 1.0,
                        RenderingIntent = "Perception",
                        QualityIndex = 4
                    },
                    new WorkshopPreset
                    {
                        Name = "كانفاس / بانر",
                        Category = "Canvas",
                        Description = "طباعة على كانفاس أو بانر خارجي — مقاومة للأشعة",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 110,
                        BrightCyan = 40,
                        BrightMagenta = 40,
                        TotalInkMaximum = 320,
                        GateLm = 0.5,
                        RenderingIntent = "Saturation",
                        QualityIndex = 2,
                        ChannelLimitK = 220,
                        Notes = "كانفاس = تبريد بطيء → حبر أكثر"
                    },

                    // ── film / screen ─────────────────────
                    new WorkshopPreset
                    {
                        Name = "أفلام سلك سكرين — كثافة عالية",
                        Category = "Film",
                        Description = "أفلام الطباعة اليدوية (Screen) — كثافة نقط عالية جداً",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 150,
                        BrightCyan = 30,
                        BrightMagenta = 0.0,  // ⭐ تصفير كامل للماجنتا الفاتحة
                        TotalInkMaximum = 250,
                        GateLm = 0.0,          // ⭐ تصفير Lm
                        SmartLmEnabled = false,
                        RenderingIntent = "ColorimetricAbsolute",
                        QualityIndex = 5,
                        ChannelLimitC = 150,
                        ChannelLimitM = 150,
                        ChannelLimitY = 200,
                        ChannelLimitK = 200,
                        Bits = 2,
                        Notes = "للأفلام — الكثافة أهم من الألوان"
                    },
                    new WorkshopPreset
                    {
                        Name = "أفلام سلك سكرين — سرعة",
                        Category = "Film",
                        Description = "draft mode للأفلام — سرعة بدون جودة",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 100,
                        BrightMagenta = 0.0,
                        TotalInkMaximum = 200,
                        GateLm = 0.0,
                        RenderingIntent = "NoCorrection",
                        QualityIndex = 1,
                        Bits = 1,
                        Notes = "draft — للفحص السريع"
                    },

                    // ── cardstock / paper ──────────────────
                    new WorkshopPreset
                    {
                        Name = "ورق عادي — ألوان صافية",
                        Category = "Plain",
                        Description = "ورق A4 عادي — ألوان صافية، تباين جيد",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 100,
                        BrightCyan = 50,
                        BrightMagenta = 50,
                        TotalInkMaximum = 300,
                        GateLm = 1.0,
                        RenderingIntent = "Perception",
                        QualityIndex = 3
                    },
                    new WorkshopPreset
                    {
                        Name = "كارتستوك — ألوان ممتلئة",
                        Category = "Cardstock",
                        Description = "للطباعة على كارتستوك سميك — ألوان غنية",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 120,
                        BrightCyan = 60,
                        BrightMagenta = 60,
                        TotalInkMaximum = 350,
                        GateLm = 1.0,
                        RenderingIntent = "PerceptionPhoto",
                        QualityIndex = 4,
                        Notes = "كارتستوك = سطح أملس = ألوان ممتلئة"
                    },
                    new WorkshopPreset
                    {
                        Name = "صورة فوتوغرافية — جودة عالية",
                        Category = "Photo",
                        Description = "طباعة صور بجودة فوتوغرافية — 1440dpi",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 80,
                        BrightCyan = 50,
                        BrightMagenta = 50,
                        TotalInkMaximum = 280,
                        GateLm = 1.0,
                        RenderingIntent = "PerceptionPhoto",
                        IccProfile = "AdobeRGB1998.icc",
                        QualityIndex = 5,
                        Bits = 2,
                        Notes = "للصور — استخدم ورق photo glossy"
                    },

                    // ── grayscale / black only ────────────
                    new WorkshopPreset
                    {
                        Name = "رمادي — نص فقط",
                        Category = "Grayscale",
                        Description = "طباعة نصوص ورسومات أبيض وأسود — توفير حبر",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 0,
                        TotalInkMaximum = 150,
                        GateC = 0, GateM = 0, GateY = 0,
                        GateLm = 0, GateLc = 0,
                        ColorMode = "GrayscaleFast",
                        RenderingIntent = "NoCorrection",
                        QualityIndex = 2,
                        Notes = "K فقط — توفير حبر 90%"
                    },

                    // ── draft / test ──────────────────────
                    new WorkshopPreset
                    {
                        Name = "draft — معايرة سريعة",
                        Category = "Test",
                        Description = "draft mode — أسرع طباعة للمعايرة",
                        PrinterDefinition = "Epson_L1300.tpp",
                        ColorTable = "DX4250.tpm",
                        BlackLevel = 100,
                        TotalInkMaximum = 200,
                        RenderingIntent = "NoCorrection",
                        QualityIndex = 0,
                        Bits = 1,
                        Notes = "draft — لا تستخدم للإنتاج"
                    }
                }
            };
        }

        // ── Save / Load ──

        /// <summary>
        /// حفظ مكتبة presets إلى ملف JSON
        /// </summary>
        public static void Save(string filePath, WorkshopPresetLibrary library)
        {
            string json = JsonSerializer.Serialize(library, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// تحميل مكتبة presets من ملف JSON
        /// </summary>
        public static WorkshopPresetLibrary? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<WorkshopPresetLibrary>(json, _jsonOptions);
        }

        /// <summary>
        /// إنشاء أو تحديث preset واحد في مكتبة موجودة
        /// </summary>
        public static WorkshopPresetLibrary UpsertPreset(
            WorkshopPresetLibrary library, WorkshopPreset preset)
        {
            var list = new System.Collections.Generic.List<WorkshopPreset>(library.Presets);
            int idx = Array.FindIndex(list.ToArray(), p => p.Name == preset.Name);
            if (idx >= 0) list[idx] = preset;
            else list.Add(preset);

            return new WorkshopPresetLibrary
            {
                Version = "2.0",
                LastModified = DateTime.Now.ToString("yyyy-MM-dd"),
                Presets = list.ToArray()
            };
        }

        /// <summary>
        /// فلترة presets حسب الفئة
        /// </summary>
        public static WorkshopPreset[] ByCategory(WorkshopPresetLibrary lib, string category) =>
            Array.FindAll(lib.Presets, p =>
                p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// البحث عن preset بالاسم
        /// </summary>
        public static WorkshopPreset? Find(WorkshopPresetLibrary lib, string name) =>
            Array.Find(lib.Presets, p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
