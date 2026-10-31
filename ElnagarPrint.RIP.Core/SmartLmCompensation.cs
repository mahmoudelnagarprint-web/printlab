using System;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  SmartLmCompensation.cs — تعويض ذكي عند تصفير Lm
    //  Phase 1: Core Engine v2
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// خاصية SmartLmCompensation: تعويض تلقائي عند تصفير Light Magenta
    ///
    /// عند إطفاء Lm (= Gate_Lm = 0) تتحول درجة البشرة (skin tones)
    /// من Light Magenta للـ Dark Magenta مباشرة — مما يسبب بهتان واضح.
    ///
    /// هذا المعالج يوزع نسبة من Lm المفقودة على الـ M الأساسي
    /// لمنع البهتان مع الحفاظ على تصفير Lm الكامل.
    ///
    /// المعادلة:
    ///   M_compensated = M_original + (Lm_original × compensation_ratio)
    ///   compensation_ratio = SmartLmRatio (0.0–1.0, default 0.35)
    ///   M_final = min(M_compensated, 1.0)
    ///
    /// التفعيل: SmartLmEnabled = true + Gate_Lm = 0
    /// </summary>
    public sealed class SmartLmCompensation
    {
        /// <summary>تفعيل التعويض الذكي</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// نسبة التعويض: كم من Lm المفقود يُضاف للـ M (0.0–1.0)
        /// 0.0 = لا تعويض (Lm مطفي ولا حاجة)
        /// 0.35 = default — يحافظ على درجة البشرة بدون تغيير ملحوظ في اللون
        /// 1.0 = تعويض كامل (Lm = M' — لا فرق عن عدم التصفير)
        /// </summary>
        public double CompensationRatio { get; set; } = 0.35;

        /// <summary>
        /// عتبة threshold: فقط المناطق الفاتحة (Lm > threshold) تتأثر
        /// هذا يمنع التعويض في المناطق الداكنة جداً حيث M=الكثير بالفعل
        /// default 0.1 — المناطق التي Lm فيها > 10% من M
        /// </summary>
        public double LmThreshold { get; set; } = 0.10;

        /// <summary>
        /// تطبيق التعويض: M + (Lm × ratio) لكل بكسل
        /// عند Gate_Lm = 0 → Lm_original هو ما كان سيُشتق من M_original
        /// </summary>
        public double ApplyCompensation(double mOriginal, double lmDerived)
        {
            if (!Enabled) return mOriginal;

            if (lmDerived <= LmThreshold)
                return mOriginal;  // منطقة فاتحة جداً — لا تعويض

            double added = lmDerived * CompensationRatio;
            return Math.Min(mOriginal + added, 1.0);
        }

        /// <summary>
        /// تطبيق التعويض على بكسل كامل InkChannels8
        /// </summary>
        public InkChannels8 ApplyToChannels(double c, double m, double y, double k,
                                             double lc, double lm,
                                             double lightMagentaFactor = 0.5)
        {
            if (!Enabled)
                return new InkChannels8 { C = c, M = m, Y = y, K = k, Lc = lc, Lm = lm };

            // lmDerived = ما كان سيُشتق من M (Lm من LmFactor × M)
            double lmDerived = m * lightMagentaFactor;

            double mComp = ApplyCompensation(m, lmDerived);

            return new InkChannels8
            {
                C  = c,
                M  = mComp,   // M مع التعويض
                Y  = y,
                K  = k,
                Lc = lc,
                Lm = 0        // Lm still zeroed
            };
        }

        /// <summary>
        /// حساب factor الأمثل للتعويض بناءً على عينة ألوان
        /// يستخدم عينة مرجعية من ألوان البشرة لتحديد best ratio
        /// </summary>
        public static double CalibrateOptimalRatio(
            (double r, double g, double b)[] skinSamples,
            double lightMagentaFactor = 0.5)
        {
            // عينة ألوان البشرة المرجعية من PrintFab calibration
            // Haut_01: RGB(239, 220, 210) — فاتح
            // Haut_02: RGB(204, 165, 142) — متوسط
            // Haut_03: RGB(152, 104, 79)  — غامق

            double bestRatio = 0.35;  // default
            double minError = double.MaxValue;

            for (double ratio = 0.0; ratio <= 0.8; ratio += 0.05)
            {
                double totalError = 0;
                foreach (var (r, g, b) in skinSamples)
                {
                    // RGB → CMYK
                    double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
                    double k = Math.Max(Math.Max(rn, gn), bn);
                    double m = (k - gn) / k;
                    double lm = m * lightMagentaFactor;  // Lm derived

                    // Apply compensation
                    double mNew = Math.Min(m + lm * ratio, 1.0);

                    // Reconstruct RGB
                    double ki = 1.0 - k;
                    double rNew = Math.Clamp((1.0 - 0) * ki * 255, 0, 255);  // C=0
                    double gNew = Math.Clamp((1.0 - mNew) * ki * 255, 0, 255);
                    double bNew = Math.Clamp((1.0 - 0) * ki * 255, 0, 255);  // Y=0

                    double error = Math.Sqrt(
                        (r - rNew) * (r - rNew) +
                        (g - gNew) * (g - gNew) +
                        (b - bNew) * (b - bNew));
                    totalError += error;
                }

                if (totalError < minError)
                {
                    minError = totalError;
                    bestRatio = ratio;
                }
            }

            return bestRatio;
        }

        /// <summary>
        /// إعدادات SmartLm جاهزة للـ L1800 (ورق عادي)
        /// </summary>
        public static SmartLmCompensation DefaultL1800 => new()
        {
            Enabled = false,        // يُفعل يدوياً عند الحاجة
            CompensationRatio = 0.35,
            LmThreshold = 0.10
        };
    }
}
