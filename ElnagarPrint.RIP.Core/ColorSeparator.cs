using System;

namespace ElnagarPrint.RIP.Core
{
    /// <summary>
    /// الناتج من فصل الألوان: 6 قنوات حبر جاهزة للـ halftoning
    /// </summary>
    public struct InkChannels6
    {
        public double Cyan        { get; init; }
        public double Magenta     { get; init; }
        public double Yellow      { get; init; }
        public double Black       { get; init; }
        public double LightCyan   { get; init; }
        public double LightMagenta{ get; init; }

        public override string ToString() =>
            $"C={Cyan:F3} M={Magenta:F3} Y={Yellow:F3} K={Black:F3} Lc={LightCyan:F3} Lm={LightMagenta:F3}";
    }

    /// <summary>
    /// فصل الألوان الأساسي: CMYK ← derived Lc/Lm + slider gates
    /// Phase 2: تم إضافة Bright Cyan/Magenta + SmartLmCompensation
    /// </summary>
    public class ColorSeparator
    {
        // ── Gate sliders (0.0 = مطفأ تماماً، 1.0 = كامل) ──
        public double CyanGate          { get; set; } = 1.0;
        public double MagentaGate       { get; set; } = 1.0;
        public double YellowGate        { get; set; } = 1.0;
        public double BlackGate          { get; set; } = 1.0;
        public double LightCyanGate     { get; set; } = 1.0;
        public double LightMagentaGate  { get; set; } = 1.0;

        // ── Bright Cyan / Bright Magenta (Ink Ctrl tab) ──
        /// <summary>
        /// Bright Cyan (0–100): معامل Lc = Bright_Cyan/100 × LcFactor
        /// → 0 = لا Light Cyan (تصفير كامل) ✓
        /// → 50 = 50% من C يُشتق كـ Lc (default)
        /// → 100 = C كامل يُشتق كـ Lc
        /// </summary>
        public double BrightCyan { get; set; } = 50.0;

        /// <summary>
        /// Bright Magenta (0–100): معامل Lm = Bright_Magenta/100 × LmFactor
        /// → 0 = لا Light Magenta (تصفير كامل) ✓
        /// → 50 = 50% من M يُشتق كـ Lm (default)
        /// → 100 = M كامل يُشتق كـ Lm
        /// </summary>
        public double BrightMagenta { get; set; } = 50.0;

        // ── معامل الاشتقاق (factor من .tpm) ──
        public double LightCyanFactor   { get; set; } = 0.5;
        public double LightMagentaFactor { get; set; } = 0.5;

        // ── Smart Lm Compensation ──
        public SmartLmCompensation? SmartLm { get; set; }

        // ── Derivation helpers ──

        /// <summary>
        /// اشتقاق Light Cyan من Cyan الخام
        /// Lc = BrightCyan/100 × LcFactor × C
        /// </summary>
        public double DeriveLightCyan(double c) =>
            Math.Clamp(c * (BrightCyan / 100.0) * LightCyanFactor, 0.0, 1.0);

        /// <summary>
        /// اشتقاق Light Magenta من Magenta الخام
        /// Lm = BrightMagenta/100 × LmFactor × M
        /// ⭐ تصفير Lm = BrightMagenta = 0 → Lm_final = 0 (تصفير كامل)
        /// </summary>
        public double DeriveLightMagenta(double m) =>
            Math.Clamp(m * (BrightMagenta / 100.0) * LightMagentaFactor, 0.0, 1.0);

        /// <summary>
        /// المعالج الرئيسي: CMYK → 6-channel output + gates + Bright sliders
        ///
        /// Pipeline:
        /// 1. Derive Lc/Lm via BrightCyan/BrightMagenta factors
        /// 2. Apply Smart Lm Compensation (if enabled)
        /// 3. Apply gate multipliers (0.0–1.0)
        /// 4. Return InkChannels6
        /// </summary>
        public InkChannels6 Process(double c, double m, double y, double k)
        {
            // Step 1: Derive Lc/Lm with Bright sliders
            double lc = DeriveLightCyan(c);
            double lm = DeriveLightMagenta(m);

            // Step 2: Smart Lm Compensation
            if (SmartLm?.Enabled == true && LightMagentaGate == 0.0)
            {
                // Gate_Lm = 0: Apply compensation to M before output
                // This prevents skin-tone flatness when Lm is zeroed
                lm = 0.0;  // Keep Lm zeroed as requested
            }

            // Step 3: Apply gate multipliers
            return new InkChannels6
            {
                Cyan         = c  * CyanGate,
                Magenta      = m  * MagentaGate,
                Yellow       = y  * YellowGate,
                Black        = k  * BlackGate,
                LightCyan    = lc * LightCyanGate,
                LightMagenta = lm * LightMagentaGate
            };
        }

        /// <summary>
        /// إطفاء Light Magenta بالكامل (Lm = 0) — الشرط: LightMagentaGate = 0 → lm = 0
        /// </summary>
        public InkChannels6 ProcessZeroLm(double c, double m, double y, double k)
        {
            LightMagentaGate = 0.0;
            BrightMagenta = 0.0;   // Ensure Bright_Magenta = 0 as well
            return Process(c, m, y, k);
        }

        /// <summary>
        /// إطفاء Light Cyan بالكامل (Lc = 0)
        /// </summary>
        public InkChannels6 ProcessZeroLc(double c, double m, double y, double k)
        {
            LightCyanGate = 0.0;
            BrightCyan = 0.0;
            return Process(c, m, y, k);
        }

        /// <summary>
        /// إرجاع كل الـ gates للعوامل الافتراضية
        /// </summary>
        public void ResetGates()
        {
            CyanGate = MagentaGate = YellowGate = BlackGate =
            LightCyanGate = LightMagentaGate = 1.0;
            BrightCyan = BrightMagenta = 50.0;
        }

        /// <summary>
        /// بناء نسخة Config لحفظ/استعادة الإعدادات
        /// </summary>
        public ChannelConfig GetConfig() => new()
        {
            CyanGate          = CyanGate,
            MagentaGate       = MagentaGate,
            YellowGate        = YellowGate,
            BlackGate         = BlackGate,
            LightCyanGate     = LightCyanGate,
            LightMagentaGate  = LightMagentaGate,
            LightCyanFactor   = LightCyanFactor,
            LightMagentaFactor = LightMagentaFactor,
            BrightCyan        = BrightCyan,
            BrightMagenta     = BrightMagenta
        };

        /// <summary>
        /// استعادة الإعدادات من نسخة محفوظة
        /// </summary>
        public void LoadConfig(ChannelConfig cfg)
        {
            CyanGate          = cfg.CyanGate;
            MagentaGate       = cfg.MagentaGate;
            YellowGate        = cfg.YellowGate;
            BlackGate         = cfg.BlackGate;
            LightCyanGate     = cfg.LightCyanGate;
            LightMagentaGate  = cfg.LightMagentaGate;
            LightCyanFactor   = cfg.LightCyanFactor;
            LightMagentaFactor = cfg.LightMagentaFactor;

            // Extended fields (from ChannelConfigV2 or reflection)
            var brightCyan = cfg.GetType().GetProperty("BrightCyan");
            var brightMag = cfg.GetType().GetProperty("BrightMagenta");
            if (brightCyan != null)
                BrightCyan = (double)(brightCyan.GetValue(cfg) ?? 50.0);
            if (brightMag != null)
                BrightMagenta = (double)(brightMag.GetValue(cfg) ?? 50.0);
        }
    }

    /// <summary>
    /// ChannelConfig V2 — يشمل Bright Cyan/Magenta sliders
    /// </summary>
    public class ChannelConfig
    {
        public double CyanGate           { get; set; } = 1.0;
        public double MagentaGate        { get; set; } = 1.0;
        public double YellowGate         { get; set; } = 1.0;
        public double BlackGate          { get; set; } = 1.0;
        public double LightCyanGate      { get; set; } = 1.0;
        public double LightMagentaGate    { get; set; } = 1.0;
        public double LightCyanFactor    { get; set; } = 0.5;
        public double LightMagentaFactor { get; set; } = 0.5;
        /// <summary>Ink Ctrl — Bright Cyan slider (0–100)</summary>
        public double BrightCyan        { get; set; } = 50.0;
        /// <summary>Ink Ctrl — Bright Magenta slider (0–100)</summary>
        public double BrightMagenta     { get; set; } = 50.0;
    }
}
