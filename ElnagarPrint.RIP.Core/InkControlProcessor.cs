using System;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  InkControlProcessor.cs — التحكم بالحبر: UCR + حدود + تصحيح يدوي
    //  Phase 1: Core Engine v2
    // ══════════════════════════════════════════════════════════════════════════

    #region ─── Settings ───────────────────────────────────────────────────────

    /// <summary>
    /// إعدادات التحكم بالحبر — Ink Ctrl tab
    /// </summary>
    public sealed class InkControlSettings
    {
        // ── UCR / GCR (Black Level) ──
        /// <summary>
        /// مستوى UCR (Under Color Removal): 0–200 (100 = default)
        /// 0   = لا إزالة: CMY+K معًا في الظلال
        /// 100 = إزالة كاملة: K فقط في الظلال
        /// 200 = زيادة مفرطة: K زائد CMY خفيفة
        /// </summary>
        public double BlackLevelUCR { get; set; } = 100.0;

        // ── Bright Cyan / Bright Magenta (Ink Ctrl tab) ──
        /// <summary>Bright Cyan (0–100): معامل Lc = Bright_Cyan/100 × Lc_factor</summary>
        public double BrightCyan { get; set; } = 50.0;

        /// <summary>Bright Magenta (0–100): معامل Lm = Bright_Magenta/100 × Lm_factor</summary>
        public double BrightMagenta { get; set; } = 50.0;

        // ── Ink Limits ──
        /// <summary>
        /// الحد الأقصى للحبر الكلي (100–400%, default 300%)
        /// </summary>
        public double TotalInkMaximum { get; set; } = 300.0;

        /// <summary>حد الحبر لقناة C (0–200%, default 100%)</summary>
        public double ChannelLimitC { get; set; } = 200.0;
        /// <summary>حد الحبر لقناة M (0–200%, default 100%)</summary>
        public double ChannelLimitM { get; set; } = 200.0;
        /// <summary>حد الحبر لقناة Y (0–200%, default 100%)</summary>
        public double ChannelLimitY { get; set; } = 200.0;
        /// <summary>حد الحبر لقناة K (0–200%, default 100%)</summary>
        public double ChannelLimitK { get; set; } = 200.0;
        /// <summary>حد الحبر لقناة Lc (0–200%, default 100%)</summary>
        public double ChannelLimitLc { get; set; } = 200.0;
        /// <summary>حد الحبر لقناة Lm (0–200%, default 100%)</summary>
        public double ChannelLimitLm { get; set; } = 200.0;

        /// <summary>
        /// Black Ink Limit (0–200%): يتحكم بكم CMY يُضاف للـ K في الظلال الداكنة
        /// </summary>
        public double BlackInkLimit { get; set; } = 100.0;

        // ── Per-channel Color Correction (Correction tab) ──
        /// <summary>تصحيح القناة السوداء (0–200, 100=طبيعي)</summary>
        public double CorrectionBlack  { get; set; } = 100.0;
        /// <summary>تصحيح القناة الفيروزية (0–200, 100=طبيعي)</summary>
        public double CorrectionCyan   { get; set; } = 100.0;
        /// <summary>تصحيح القناة القرمزية (0–200, 100=طبيعي)</summary>
        public double CorrectionMagenta { get; set; } = 100.0;
        /// <summary>تصحيح القناة الصفراء (0–200, 100=طبيعي)</summary>
        public double CorrectionYellow  { get; set; } = 100.0;

        /// <summary>تفعيل التصحيح اليدوي لكل قناة</summary>
        public bool UseIndividualCorrection { get; set; } = false;
    }

    #endregion

    #region ─── Ink Channels 8 — with Lc + Lm ────────────────────────────────

    /// <summary>
    /// 8-channel ink values for 6-color printers (L1800)
    /// </summary>
    public readonly struct InkChannels8
    {
        public double C  { get; init; }
        public double M  { get; init; }
        public double Y  { get; init; }
        public double K  { get; init; }
        public double Lc { get; init; }
        public double Lm { get; init; }

        public double Total => C + M + Y + K + Lc + Lm;

        public InkChannels8 Clamp() => new()
        {
            C  = Math.Clamp(C,  0, 1),
            M  = Math.Clamp(M,  0, 1),
            Y  = Math.Clamp(Y,  0, 1),
            K  = Math.Clamp(K,  0, 1),
            Lc = Math.Clamp(Lc, 0, 1),
            Lm = Math.Clamp(Lm, 0, 1)
        };

        public override string ToString() =>
            $"C={C:F3} M={M:F3} Y={Y:F3} K={K:F3} Lc={Lc:F3} Lm={Lm:F3}";
    }

    #endregion

    #region ─── Processor ──────────────────────────────────────────────────────

    /// <summary>
    /// محرك التحكم بالحبر — يطبق UCR + Ink Limits + Correction + Bright Cyan/Magenta
    /// </summary>
    public sealed class InkControlProcessor
    {
        public InkControlSettings Settings { get; }

        // Lc/Lm derivation factors from .tpm (default 0.5)
        public double LcFactor { get; set; } = 0.5;
        public double LmFactor { get; set; } = 0.5;

        public InkControlProcessor() => Settings = new InkControlSettings();
        public InkControlProcessor(InkControlSettings s) => Settings = s;

        // ── Main Process: 4-channel CMYK → 6-channel output ──

        /// <summary>
        /// المعالج الرئيسي: CMYK + Lc/Lm derivation → 8-channel output
        ///
        /// Pipeline:
        /// 1. Derive Lc/Lm from C/M × Bright factors × Lc/Lm factors
        /// 2. Apply per-channel correction (Correction tab)
        /// 3. Apply UCR / Black Level
        /// 4. Apply ink limits (total + per-channel + BlackInkLimit)
        ///
        /// Note: Bright Cyan/Magenta sliders are APPLIED BEFORE the InkControlProcessor
        /// (in ColorSeparator.cs). This processor handles the FIXED per-channel corrections.
        /// </summary>
        public InkChannels8 Process(double c, double m, double y, double k)
        {
            // Step 1: Derive Lc/Lm (Light inks)
            double lc = Math.Clamp(c * (Settings.BrightCyan / 100.0) * LcFactor, 0, 1);
            double lm = Math.Clamp(m * (Settings.BrightMagenta / 100.0) * LmFactor, 0, 1);

            // Step 2: Apply per-channel correction (if enabled)
            if (Settings.UseIndividualCorrection)
            {
                c = c * (Settings.CorrectionCyan / 100.0);
                m = m * (Settings.CorrectionMagenta / 100.0);
                y = y * (Settings.CorrectionYellow / 100.0);
                k = k * (Settings.CorrectionBlack / 100.0);
            }

            // Step 3: Apply UCR / GCR (Black Level)
            (c, m, y, k) = ApplyUCR(c, m, y, k, Settings.BlackLevelUCR);

            // Step 4: Apply Black Ink Limit (CMY added to K in shadows)
            (k, c, m, y) = ApplyBlackInkLimit(k, c, m, y, Settings.BlackInkLimit);

            // Step 5: Apply per-channel limits
            c  = Math.Min(c,  Settings.ChannelLimitC  / 100.0);
            m  = Math.Min(m,  Settings.ChannelLimitM  / 100.0);
            y  = Math.Min(y,  Settings.ChannelLimitY  / 100.0);
            k  = Math.Min(k,  Settings.ChannelLimitK  / 100.0);
            lc = Math.Min(lc, Settings.ChannelLimitLc / 100.0);
            lm = Math.Min(lm, Settings.ChannelLimitLm / 100.0);

            // Step 6: Apply total ink limit
            var ink8 = new InkChannels8 { C = c, M = m, Y = y, K = k, Lc = lc, Lm = lm }.Clamp();
            ink8 = ApplyTotalInkLimit(ink8, Settings.TotalInkMaximum);

            return ink8;
        }

        // ── UCR / GCR ──

        /// <summary>
        /// تطبيق UCR (Under Color Removal) — Black Level slider
        ///
        /// المعادلة:
        ///   UCR_factor = Black_Level / 100.0    (0.0–2.0)
        ///   K_gray     = min(C,M,Y) × UCR_factor
        ///   C_out      = max(C - K_gray, 0)
        ///   M_out      = max(M - K_gray, 0)
        ///   Y_out      = max(Y - K_gray, 0)
        ///   K_out      = K + K_gray
        ///
        /// UCR = 0   → K_gray = 0, CMY+K معًا (maximum ink)
        /// UCR = 100 → K_gray = min(C,M,Y), K replaces CMY (standard)
        /// UCR = 200 → K_gray = 2×min(C,M,Y), K dominates (heavy black)
        /// </summary>
        public static (double C, double M, double Y, double K) ApplyUCR(
            double c, double m, double y, double k, double blackLevel)
        {
            double ucr = blackLevel / 100.0;  // 0.0–2.0
            double kGray = Math.Min(Math.Min(c, m), y) * ucr;

            return (
                Math.Max(c - kGray, 0),
                Math.Max(m - kGray, 0),
                Math.Max(y - kGray, 0),
                k + kGray
            );
        }

        // ── Total Ink Limit ──

        /// <summary>
        /// تطبيق Total Ink Maximum (100–400%)
        /// إذا كان المجموع > الحد → scale كل القنوات بنفس النسبة
        /// </summary>
        public static InkChannels8 ApplyTotalInkLimit(InkChannels8 ink, double totalInkMax)
        {
            // totalInkMax is in percent (100–400), convert to 0.0–4.0
            double maxTotal = totalInkMax / 100.0;

            if (ink.Total <= maxTotal)
                return ink;

            double scale = maxTotal / ink.Total;
            return new InkChannels8
            {
                C  = ink.C  * scale,
                M  = ink.M  * scale,
                Y  = ink.Y  * scale,
                K  = ink.K  * scale,
                Lc = ink.Lc * scale,
                Lm = ink.Lm * scale
            };
        }

        // ── Black Ink Limit ──

        /// <summary>
        /// تطبيق Black Ink Limit — يضيف CMY للـ K في الظلال الداكنة
        ///
        /// المعادلة:
        ///   K_final = K + (min(C,M,Y) × Black_Ink_Limit/100)
        ///   C_final = max(C - min(C,M,Y) × Black_Ink_Limit/100, 0) — same for M,Y
        /// </summary>
        public static (double K, double C, double M, double Y) ApplyBlackInkLimit(
            double k, double c, double m, double y, double blackInkLimit)
        {
            if (blackInkLimit <= 0) return (k, c, m, y);

            double factor = blackInkLimit / 100.0;
            double minCMY = Math.Min(Math.Min(c, m), y);
            double addK = minCMY * factor;

            return (
                k + addK,
                Math.Max(c - addK, 0),
                Math.Max(m - addK, 0),
                Math.Max(y - addK, 0)
            );
        }

        // ── Ink Cost Estimation ──

        /// <summary>
        /// تقدير استهلاك الحبر بالملليلتر لصورة بحجم width×height px
        /// باستخدام LUT density من .tpm (32-entry table)
        ///
        /// المعادلة:
        ///   avg_density_per_channel = sum(Density[ch][i] for i 0..31) / 32
        ///   pixel_count = width × height
        ///   ink_ml[ch] = (avg_density/255) × pixel_count × droplet_volume_pl / 1_000_000
        ///
        /// droplet_volume_pl: 3pl (L1800 small), 6pl (medium), 10pl (large)
        /// </summary>
        public static InkCostResult EstimateInkCost(
            InkChannels8 totalInk,
            int widthPx, int heightPx,
            double smallDropletPl = 3.0,
            double mediumDropletPl = 6.0,
            double largeDropletPl = 10.0,
            double? customInkPerMl = null) // سعر الملليلتر بالجنيه
        {
            long pixels = (long)widthPx * heightPx;

            // Average ink density per channel (normalized 0–1)
            // Level 100% (max) = large droplet, Level 0% = no ink
            // Approximation: use total ink fraction × droplet size multiplier
            double avgDensityC  = totalInk.C  * 0.7 + totalInk.Lc * 0.5;
            double avgDensityM  = totalInk.M  * 0.7 + totalInk.Lm * 0.5;
            double avgDensityY  = totalInk.Y  * 0.6;
            double avgDensityK  = totalInk.K  * 0.9;  // K is denser

            // Convert to nanoliters: pixel_count × avg_density × droplet_pl
            double nlC  = pixels * avgDensityC  * mediumDropletPl;
            double nlM  = pixels * avgDensityM  * mediumDropletPl;
            double nlY  = pixels * avgDensityY  * mediumDropletPl;
            double nlK  = pixels * avgDensityK  * largeDropletPl;
            double nlLc = pixels * totalInk.Lc   * smallDropletPl;
            double nlLm = pixels * totalInk.Lm   * smallDropletPl;

            // Convert to milliliters
            double mlC  = nlC  / 1_000_000.0;
            double mlM  = nlM  / 1_000_000.0;
            double mlY  = nlY  / 1_000_000.0;
            double mlK  = nlK  / 1_000_000.0;
            double mlLc = nlLc / 1_000_000.0;
            double mlLm = nlLm / 1_000_000.0;

            double totalMl = mlC + mlM + mlY + mlK + mlLc + mlLm;

            // Cost calculation
            double? costPerMl = customInkPerMl;
            if (costPerMl.HasValue)
            {
                // Assumes all channels same price; extend for per-channel prices
            }

            return new InkCostResult
            {
                MillilitersC  = mlC,
                MillilitersM  = mlM,
                MillilitersY  = mlY,
                MillilitersK  = mlK,
                MillilitersLc = mlLc,
                MillilitersLm = mlLm,
                TotalMilliliters = totalMl,
                EstimatedCost = costPerMl.HasValue ? totalMl * costPerMl.Value : null,
                PixelCount = pixels
            };
        }
    }

    #endregion

    #region ─── Result Types ───────────────────────────────────────────────────

    /// <summary>
    /// نتيجة تقدير استهلاك الحبر
    /// </summary>
    public sealed class InkCostResult
    {
        public double MillilitersC  { get; init; }
        public double MillilitersM  { get; init; }
        public double MillilitersY  { get; init; }
        public double MillilitersK  { get; init; }
        public double MillilitersLc { get; init; }
        public double MillilitersLm { get; init; }
        public double TotalMilliliters { get; init; }
        public double? EstimatedCost { get; init; }   // جنيه (if price provided)
        public long PixelCount { get; init; }

        public override string ToString() =>
            $"Total={TotalMilliliters:F2}ml  C={MillilitersC:F2}ml M={MillilitersM:F2}ml Y={MillilitersY:F2}ml K={MillilitersK:F2}ml Lc={MillilitersLc:F2}ml Lm={MillilitersLm:F2}ml";
    }

    #endregion
}
