using System;
using System.Collections.Generic;
using System.Linq;

namespace ElnagarPrint.RIP.Core
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  نواتج وحدة التنقيط
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ناتج التنقيط لقناة واحدة: خريطة النقاط النهائية (0 = لا قطرة، 1 = قطرة صغيرة، 2 = قطرة متوسطة، 3 = قطرة كبيرة)
    /// </summary>
    public class HalftonedChannel
    {
        /// <summary>عرض الصورة بالبكسل</summary>
        public int Width  { get; init; }
        /// <summary>ارتفاع الصورة بالبكسل</summary>
        public int Height { get; init; }

        /// <summary>
        /// خريطة النقاط: [row * Width + col]
        /// القيم: 0 = لا قطرة | 1 = صغيرة | 2 = متوسطة | 3 = كبيرة
        /// </summary>
        public byte[] DotMap { get; init; }

        public byte GetDot(int row, int col) => DotMap[row * Width + col];
    }

    /// <summary>
    /// ناتج التنقيط للـ 6 قنوات (CMYK + Lc + Lm)
    /// </summary>
    public class HalftoneResult
    {
        public HalftonedChannel Cyan          { get; init; }
        public HalftonedChannel Magenta       { get; init; }
        public HalftonedChannel Yellow        { get; init; }
        public HalftonedChannel Black         { get; init; }
        public HalftonedChannel LightCyan    { get; init; }
        public HalftonedChannel LightMagenta  { get; init; }

        /// <summary>عرض الصورة</summary>
        public int Width  => Cyan.Width;
        /// <summary>ارتفاع الصورة</summary>
        public int Height => Cyan.Height;

        /// <summary>الوصول للقنوات بالأ индекс (0–5)</summary>
        public HalftonedChannel[] Channels => [Cyan, Magenta, Yellow, Black, LightCyan, LightMagenta];

        public HalftonedChannel this[int index] => Channels[index];

        /// <summary>استرجاع DotMap لقناة معينة بالأ اسم</summary>
        public byte[] GetDotMap(string channel)
        {
            return channel.ToUpperInvariant() switch
            {
                "C" or "CYAN"         => Cyan.DotMap,
                "M" or "MAGENTA"      => Magenta.DotMap,
                "Y" or "YELLOW"       => Yellow.DotMap,
                "K" or "BLACK"        => Black.DotMap,
                "LC" or "LIGHTCYAN"   => LightCyan.DotMap,
                "LM" or "LIGHTMAGENTA"=> LightMagenta.DotMap,
                _ => throw new ArgumentOutOfRangeException(nameof(channel))
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  إعدادات خوارزمية Floyd-Steinberg
    // ─────────────────────────────────────────────────────────────────────────────

    public enum DropletSize
    {
        None = 0,
        Small = 1,   // 3 pl  (بيكولتر)
        Medium = 2,  // 6 pl
        Large = 3    // 10 pl
    }

    public class HalftoneSettings
    {
        /// <summary>تفعيل التنقيط متعدد المستويات (Variable Droplet) — Default: true</summary>
        public bool UseMultiLevel { get; set; } = true;

        /// <summary>نسبة العتبة للقنوات الفاتحة (Lc/Lm) — Default: 0.45</summary>
        public double LightChannelThreshold { get; set; } = 0.45;

        /// <summary>نسبة العتبة للقنوات الغامقة — Default: 0.5</summary>
        public double DarkChannelThreshold { get; set; } = 0.50;

        /// <summary>نسبة العتبة للقناة الصفراء (أكثر حساسية للعين) — Default: 0.48</summary>
        public double YellowThreshold { get; set; } = 0.48;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Variable Droplet Sizes — مستويات الحجم من LUT .tpm
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// مستويات حجم القطرة من LUT الـ .tpm:
    ///   Level 0 → 0%   → لا قطرة
    ///   Level 1 → 30%  → صغيرة  (3 pl)
    ///   Level 2 → 60%  → متوسطة (6 pl)
    ///   Level 3 → 100% → كبيرة  (10 pl)
    /// </summary>
    public static class DotSizeLevels
    {
        /// <summary>نسب العتبة لكل مستوى (0–1.0)</summary>
        public static readonly double[] Thresholds = { 0.0, 0.30, 0.60, 1.00 };

        /// <summary>حجم القطرة بالبيكولتر لكل مستوى</summary>
        public static readonly int[] DropletPicoliters = { 0, 3, 6, 10 };

        /// <summary>تحويل مستوى النقطة (0–3) إلى حجم بالبيكولتر</summary>
        public static int ToPicoliters(byte level) =>
            level >= 0 && level < DropletPicoliters.Length
                ? DropletPicoliters[level]
                : 0;

        /// <summary>اختيار المستوى بناءً على قيمة البكسل (0–1)</summary>
        public static byte FromPixelValue(double pixel)
        {
            if (pixel < Thresholds[1]) return 0;
            if (pixel < Thresholds[2]) return 1;
            if (pixel < Thresholds[3]) return 2;
            return 3;
        }
    }

    /// <summary>
    /// ملخص حجم القطرة لقناة واحدة — يُستخدم لبناء خريطة VDS
    /// </summary>
    public sealed class ChannelDropletSummary
    {
        public string Channel     { get; init; } = "";
        public int    SmallDots   { get; set; }
        public int    MediumDots  { get; set; }
        public int    LargeDots   { get; set; }
        public int    TotalPixels { get; set; }

        public double SmallRatio  => TotalPixels > 0 ? SmallDots  / (double)TotalPixels : 0;
        public double MediumRatio => TotalPixels > 0 ? MediumDots / (double)TotalPixels : 0;
        public double LargeRatio  => TotalPixels > 0 ? LargeDots  / (double)TotalPixels : 0;
    }

    /// <summary>
    /// محلل VDS — إحصائيات الأحجام لكل القنوات
    /// </summary>
    public static class VDSAnalyzer
    {
        /// <summary>
        /// تحليل ملخص كامل لكل القنوات الستة
        /// </summary>
        public static ChannelDropletSummary[] AnalyzeAllChannels(HalftoneResult result)
        {
            string[] names = ["Cyan", "Magenta", "Yellow", "Black", "LightCyan", "LightMagenta"];
            var all = new ChannelDropletSummary[6];

            for (int i = 0; i < 6; i++)
            {
                var ch = result[i];
                int small = 0, medium = 0, large = 0;
                foreach (byte b in ch.DotMap)
                {
                    if (b == 1) small++;
                    else if (b == 2) medium++;
                    else if (b == 3) large++;
                }
                all[i] = new ChannelDropletSummary
                {
                    Channel     = names[i],
                    TotalPixels = ch.Width * ch.Height,
                    SmallDots   = small,
                    MediumDots  = medium,
                    LargeDots   = large
                };
            }
            return all;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  معالج التنقيط: Floyd-Steinberg Dithering + Variable Droplet Support
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// وحدة التنقيط (Halftoning) لطابعات الإنجكت:
    /// • Floyd-Steinberg Error Diffusion لكل قناة لونية
    /// • Multi-Level Droplet Sizes (Small/Medium/Large) جاهزة للتوسع
    /// </summary>
    public class HalftoneProcessor
    {
        private readonly HalftoneSettings _settings;

        public HalftoneProcessor() : this(new HalftoneSettings()) { }

        public HalftoneProcessor(HalftoneSettings settings)
        {
            _settings = settings ?? new HalftoneSettings();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  المدخل الرئيسي: Process6Channels
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// تنقيط 6 قنوات معاً (CMYK + Lc + Lm) بصورة موحدة
        /// </summary>
        public HalftoneResult Process6Channels(
            InkChannels6[,] inputGrid)
        {
            int height = inputGrid.GetLength(0);
            int width  = inputGrid.GetLength(1);

            var c  = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].Cyan,         width, height);
            var m  = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].Magenta,       width, height);
            var y  = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].Yellow,        width, height);
            var k  = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].Black,         width, height);
            var lc = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].LightCyan,      width, height);
            var lm = FloydSteinberg(inputGrid, (g, r, c2) => g[r, c2].LightMagenta,  width, height);

            // ترتيب القنوات حسب أولوية الحبر (K أول، ثم CMY، ثم Lc/Lm)
            // Lm = 0 ممكن يتحقق هنا مباشرة من inputGrid[].LightMagentaGate=0
            return new HalftoneResult
            {
                Cyan         = c,
                Magenta      = m,
                Yellow       = y,
                Black        = k,
                LightCyan    = lc,
                LightMagenta = lm
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Floyd-Steinberg Error Diffusion (per channel)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// تطبيق Floyd-Steinberg على قناة واحدة.
        /// </summary>
        private HalftonedChannel FloydSteinberg(
            InkChannels6[,] grid,
            Func<InkChannels6[,], int, int, double> getValue,
            int width,
            int height)
        {
            // مصفوفة الأخطاء المتراكمة (float)
            var error = new double[height, width];
            var dotMap = new byte[height * width];

            for (int r = 0; r < height; r++)
            {
                for (int c2 = 0; c2 < width; c2++)
                {
                    double pixel = getValue(grid, r, c2) + error[r, c2];

                    // تحديد العتبة حسب القناة
                    double threshold = SelectThreshold(grid[r, c2]);

                    // التحويل متعدد المستويات أو ثنائي
                    byte dotLevel;
                    if (_settings.UseMultiLevel)
                    {
                        dotLevel = QuantizeMultiLevel(pixel, threshold);
                    }
                    else
                    {
                        dotLevel = QuantizeBinary(pixel, threshold);
                    }

                    // حساب الخطأ (الفروق بين القيمة الأصلية والناتجة)
                    double quantized = dotLevel / 3.0; // 0→0, 1→0.333, 2→0.666, 3→1.0
                    double quantError = Math.Clamp(pixel, 0.0, 1.0) - quantized;

                    // توزيع الخطأ على البكسلات المجاورة (Floyd-Steinberg weights)
                    //     [c2] [c2+1]
                    // [r+1]  7/16  3/16
                    // [r+1,c2-1] 3/16  5/16  1/16
                    DistributeError(error, width, height, r, c2 + 1, quantError * 7.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2 - 1, quantError * 3.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2,     quantError * 5.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2 + 1, quantError * 1.0 / 16.0);

                    dotMap[r * width + c2] = dotLevel;
                }
            }

            return new HalftonedChannel
            {
                Width  = width,
                Height = height,
                DotMap = dotMap
            };
        }

        /// <summary>
        /// توزيع الخطأ على بكسل موجود داخل حدود المصفوفة
        /// </summary>
        private static void DistributeError(double[,] error, int w, int h,
            int r, int c, double e)
        {
            if (r >= 0 && r < h && c >= 0 && c < w)
                error[r, c] += e;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  العتبات حسب نوع القناة
        // ════════════════════════════════════════════════════════════════════════

        private double SelectThreshold(InkChannels6 ch)
        {
            // نكتشف Channel type من قيمة LightCyan/LightMagenta كنسب مئوية
            // — في الاستخدام العملي هنمرر enum أو نستخدم البنية
            // مؤقتاً: استخدام عتبة القناة الفاتحة لأي قناة فيها قيمة مشتقة
            if (ch.LightCyan > 0.001 || ch.LightMagenta > 0.001)
                return _settings.LightChannelThreshold;
            if (Math.Abs(ch.Yellow - ch.Cyan) < 0.05 && ch.Yellow > 0.1)
                return _settings.YellowThreshold;
            return _settings.DarkChannelThreshold;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  التكميم (Quantization): ثنائي + متعدد المستويات
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// تنقيط ثنائي (1-bit): 0 أو 1 فقط
        /// </summary>
        private static byte QuantizeBinary(double pixel, double threshold)
        {
            return (byte)((pixel >= threshold) ? 1 : 0);
        }

        /// <summary>
        /// تنقيط متعدد المستويات (2-bit = 4 مستويات):
        ///   0 = لا قطرة        (blank)
        ///   1 = قطرة صغيرة     (~3 pl)
        ///   2 = قطرة متوسطة   (~6 pl)
        ///   3 = قطرة كبيرة    (~10 pl)
        ///
        /// الفكرة: بدلاً من عتبة واحدة، عندنا 3 حدود:
        ///   t1 (0.25) → قطرة صغيرة
        ///   t2 (0.55) → قطرة متوسطة
        ///   t3 (0.80) → قطرة كبيرة
        /// </summary>
        private static byte QuantizeMultiLevel(double pixel, double threshold)
        {
            // نستخدم نسب مئوية: pixel في مدى 0.0–1.0
            // القاعدة: L1800 بيطبع 3 أحجام قطرة
            // الحجم يعتمد على كمية الحبر المحتاجة (قيمة البكسل)
            if (pixel < 0.15) return (byte)DropletSize.None;
            if (pixel < 0.40) return (byte)DropletSize.Small;   // 3 pl
            if (pixel < 0.70) return (byte)DropletSize.Medium; // 6 pl
            return (byte)DropletSize.Large;                     // 10 pl
        }

        // ════════════════════════════════════════════════════════════════════════
        //  API إضافي: تنقيط قناة واحدة (للمعاينة أو الاختبار)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// تنقيط مصفوفة 2D لقناة واحدة (continuous tone → dots)
        /// </summary>
        public HalftonedChannel ProcessChannel(double[,] channelData)
        {
            int height = channelData.GetLength(0);
            int width  = channelData.GetLength(1);

            var error = new double[height, width];
            var dotMap = new byte[height * width];
            double threshold = _settings.DarkChannelThreshold;

            for (int r = 0; r < height; r++)
            {
                for (int c2 = 0; c2 < width; c2++)
                {
                    double pixel = channelData[r, c2] + error[r, c2];

                    byte dotLevel = _settings.UseMultiLevel
                        ? QuantizeMultiLevel(pixel, threshold)
                        : QuantizeBinary(pixel, threshold);

                    double quantized = dotLevel / 3.0;
                    double quantError = Math.Clamp(pixel, 0.0, 1.0) - quantized;

                    DistributeError(error, width, height, r, c2 + 1, quantError * 7.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2 - 1, quantError * 3.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2,     quantError * 5.0 / 16.0);
                    DistributeError(error, width, height, r + 1, c2 + 1, quantError * 1.0 / 16.0);

                    dotMap[r * width + c2] = dotLevel;
                }
            }

            return new HalftonedChannel { Width = width, Height = height, DotMap = dotMap };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ملخص: كيف نتعامل مع Variable Droplet Sizes مستقبلاً لـ L1800
        // ════════════════════════════════════════════════════════════════════════
        /*
         * ══════════════════════════════════════════════════════════════════════
         *  Variable Droplet Sizes — التصميم الجاهز للتوسع
         * ══════════════════════════════════════════════════════════════════════
         *
         *  الـ L1800 يدعم 3 أحجام قطرة (PiezoelectricDropOnDemand):
         *
         *   DropletSize.Small  =  3 pl (بيكولتر) → للتفاصيل الدقيقة +
         *                          تقليل الـ banding في gradients
         *   DropletSize.Medium =  6 pl (بيكولتر) → الاستخدام الأساسي
         *   DropletSize.Large  = 10 pl (بيكولتر) → الامتلاء السريع +
         *                          المناطق المسطحة solid
         *
         *  الكود الحالي:
         *  ─────────────
         *  • QuantizeMultiLevel() → ترجع 0/1/2/3 (4 مستويات)
         *  • DotMap يخزن byte للقيمة (0=no drop, 1=small, 2=med, 3=large)
         *  • الـ ESC/P2 encoder (الوحدة التالية) يقرأ DotMap ويحوله لـ bytes
         *    فعلية بأوامر مختلفة لكل حجم:
         *      - 0x00: no dot
         *      - 0x30: small dot (3pl)
         *      - 0x31: medium dot (6pl)
         *      - 0x32: large dot (10pl)
         *  • الترتيب: أولاً نكتب كل الـ small، ثم medium، ثم large — أو
         *    نرسل 3 passes منفصلة (Pass1: large dots → Pass2: medium → Pass3: small)
         *
         *  الميزة في الكود: Multi-level موجود فعلياً وجاهز للتفعيل.
         *  فقط احتاج نكتب ESCP2Encoder.cs (الوحدة التالية) ليقرأ الـ dot level
         *  ويحوله لأوامر الطابعة الفعلية.
         *
         *  التفعيل في ColorSeparator:
         *  ─────────────────────────
         *  LightMagentaGate = 0 → Lm في InkChannels6 = 0 → خط التنقيط
         *  يتحطم → خريطة النقاط تطلع كل的值 = 0 → الطابعة لا تطبع أي Lm.
         *
         * ══════════════════════════════════════════════════════════════════════
         */
    }
}
