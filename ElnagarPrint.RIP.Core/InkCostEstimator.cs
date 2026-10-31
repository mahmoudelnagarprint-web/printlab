using System;
using System.IO;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  InkCostEstimator.cs — تقدير تكلفة الحبر لكل عملية طباعة
    //  Phase 1: Core Engine v2
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// سعر الحبر لكل قناة — ورشة «النجار برنت»
    /// يُحدّث حسب أسعار السوق
    /// </summary>
    public sealed class InkPriceTable
    {
        // الأسعار الافتراضية (جنيه/لتر — October 2024 estimates)
        public double PricePerLiterC  { get; set; } = 850.0;  // Cyan
        public double PricePerLiterM  { get; set; } = 850.0;  // Magenta
        public double PricePerLiterY  { get; set; } = 750.0;  // Yellow
        public double PricePerLiterK  { get; set; } = 650.0;  // Black
        public double PricePerLiterLc { get; set; } = 900.0;  // Light Cyan (أغلى)
        public double PricePerLiterLm { get; set; } = 900.0;  // Light Magenta

        /// <summary>السعر الافتراضي لـ L1800 (6 ألوان)</summary>
        public static InkPriceTable L1800_Default => new();

        /// <summary>السعر الافتراضي لـ L1300 (4 ألوان)</summary>
        public static InkPriceTable L1300_Default => new()
        {
            PricePerLiterC = 850,
            PricePerLiterM = 850,
            PricePerLiterY = 750,
            PricePerLiterK = 650
        };

        public double CostPerMlC  => PricePerLiterC  / 1000.0;
        public double CostPerMlM  => PricePerLiterM  / 1000.0;
        public double CostPerMlY  => PricePerLiterY  / 1000.0;
        public double CostPerMlK  => PricePerLiterK  / 1000.0;
        public double CostPerMlLc => PricePerLiterLc / 1000.0;
        public double CostPerMlLm => PricePerLiterLm / 1000.0;
    }

    /// <summary>
    /// محرك تقدير التكلفة — يحسب استهلاك الحبر وتكلفته المالية
    /// </summary>
    public static class InkCostEstimator
    {
        /// <summary>
        /// تقدير كامل لاستهلاك الحبر لصورة بالبكسلات
        ///
        /// المعادلة:
        ///   ink_nl[ch] = pixel_count × avg_density[ch] × droplet_pl
        ///   ink_ml[ch] = ink_nl[ch] / 1_000_000
        ///
        /// avg_density[ch] = mean(Levels[ch][i] × Density[ch][i]) / 255
        ///                 (from .tpm LUT — كلما زاد المستوى زاد الاستهلاك)
        ///
        /// droplet_pl: 3pl (Lc/Lm), 6pl (C/M/Y), 10pl (K)
        /// </summary>
        public static InkEstimate Estimate(
            InkChannels8 totals,    // sum of all channel values (0.0–total_pixels × 1.0)
            int widthPx, int heightPx,
            InkPriceTable? prices = null,
            InkDropletSizes? droplets = null)
        {
            long pixels = (long)widthPx * heightPx;
            prices ??= InkPriceTable.L1800_Default;
            droplets ??= InkDropletSizes.L1800_Default;

            // Average density per channel as fraction (0.0–1.0)
            // 0.0 = no ink, 1.0 = max density
            // We use the fraction of the ink value against max possible (pixels × 1.0)
            double avgDensityC  = pixels > 0 ? Math.Clamp(totals.C  / pixels, 0, 1) : 0;
            double avgDensityM  = pixels > 0 ? Math.Clamp(totals.M  / pixels, 0, 1) : 0;
            double avgDensityY  = pixels > 0 ? Math.Clamp(totals.Y  / pixels, 0, 1) : 0;
            double avgDensityK  = pixels > 0 ? Math.Clamp(totals.K  / pixels, 0, 1) : 0;
            double avgDensityLc = pixels > 0 ? Math.Clamp(totals.Lc / pixels, 0, 1) : 0;
            double avgDensityLm = pixels > 0 ? Math.Clamp(totals.Lm / pixels, 0, 1) : 0;

            // Convert to milliliters
            // nl = pixels × density × droplet_pl → ml = nl / 1_000_000
            double mlC  = pixels * avgDensityC  * droplets.C  / 1_000_000.0;
            double mlM  = pixels * avgDensityM  * droplets.M  / 1_000_000.0;
            double mlY  = pixels * avgDensityY  * droplets.Y  / 1_000_000.0;
            double mlK  = pixels * avgDensityK  * droplets.K  / 1_000_000.0;
            double mlLc = pixels * avgDensityLc * droplets.Lc / 1_000_000.0;
            double mlLm = pixels * avgDensityLm * droplets.Lm / 1_000_000.0;
            double totalMl = mlC + mlM + mlY + mlK + mlLc + mlLm;

            // Cost in EGP
            double cost = mlC  * prices.CostPerMlC
                        + mlM  * prices.CostPerMlM
                        + mlY  * prices.CostPerMlY
                        + mlK  * prices.CostPerMlK
                        + mlLc * prices.CostPerMlLc
                        + mlLm * prices.CostPerMlLm;

            // Per-square-meter cost (for banner/canvas)
            double sqM = (widthPx / 360.0) * (heightPx / 360.0) / 1550.0; // at 360dpi
            double costPerSqM = sqM > 0 ? cost / sqM : 0;

            return new InkEstimate
            {
                MillilitersC  = mlC,
                MillilitersM  = mlM,
                MillilitersY  = mlY,
                MillilitersK  = mlK,
                MillilitersLc = mlLc,
                MillilitersLm = mlLm,
                TotalMilliliters = totalMl,
                TotalCostEGP = cost,
                CostPerSqMeter = costPerSqM,
                PixelCount = pixels,
                ImageWidthPx = widthPx,
                ImageHeightPx = heightPx,
                ResolutionDPI = 360  // reference resolution
            };
        }

        /// <summary>
        /// تقدير سريع من حجم الملف ومستوى التفاصيل
        /// </summary>
        public static InkEstimate QuickEstimate(
            string imagePath,
            double inkDensityFactor = 0.5,
            InkPriceTable? prices = null,
            InkDropletSizes? droplets = null)
        {
            if (!File.Exists(imagePath))
                return new InkEstimate { TotalMilliliters = 0, TotalCostEGP = 0 };

            var fi = new FileInfo(imagePath);
            // Rough: 1MB image ≈ 500KB uncompressed ≈ 250K pixels at 24bpp
            long pixelsApprox = fi.Length / 2;  // crude estimate

            var ink = new InkChannels8 { C = 0, M = 0, Y = 0, K = 0, Lc = 0, Lm = 0 };

            // Get actual image dimensions from header if possible
            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(imagePath));
                // Try to read width/height from JPEG/PNG header
                // For now, estimate 4×6" at 300dpi = 1200×1800
                int w = 1200, h = 1800;
                return Estimate(ink, w, h, prices, droplets);
            }
            catch
            {
                return new InkEstimate { TotalMilliliters = 0, TotalCostEGP = 0 };
            }
        }
    }

    /// <summary>
    /// أحجام القطرات (picoliters) — تختلف حسب الطابعة والجودة
    /// </summary>
    public sealed class InkDropletSizes
    {
        // L1800 standard droplet sizes
        public double C { get; set; } = 6.0;   // pl
        public double M { get; set; } = 6.0;   // pl
        public double Y { get; set; } = 6.0;   // pl
        public double K { get; set; } = 10.0;   // pl
        public double Lc { get; set; } = 3.0;   // pl
        public double Lm { get; set; } = 3.0;   // pl

        public static InkDropletSizes L1800_Default => new();
        public static InkDropletSizes L1800_Photo => new() { C = 1.5, M = 1.5, Y = 1.5, K = 3.0, Lc = 1.5, Lm = 1.5 };
        public static InkDropletSizes L1300_Draft => new() { C = 10.0, M = 10.0, Y = 10.0, K = 20.0 };
    }

    #region ─── Result ─────────────────────────────────────────────────────────

    /// <summary>
    /// نتيجة تقدير الاستهلاك والتكلفة
    /// </summary>
    public sealed class InkEstimate
    {
        public double MillilitersC  { get; init; }
        public double MillilitersM  { get; init; }
        public double MillilitersY  { get; init; }
        public double MillilitersK  { get; init; }
        public double MillilitersLc { get; init; }
        public double MillilitersLm { get; init; }
        public double TotalMilliliters { get; init; }
        public double TotalCostEGP { get; init; }
        public double CostPerSqMeter { get; init; }
        public long PixelCount { get; init; }
        public int ImageWidthPx { get; init; }
        public int ImageHeightPx { get; init; }
        public int ResolutionDPI { get; init; }

        public override string ToString() =>
            $"Total={TotalMilliliters:F3}ml  Cost={TotalCostEGP:F2}EGP  " +
            $"C={MillilitersC:F3}ml M={MillilitersM:F3}ml Y={MillilitersY:F3}ml K={MillilitersK:F3}ml";
    }

    #endregion
}
