using System;
using System.Collections.Generic;

namespace ElnagarPrint.RIP.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  LayoutComposer — استهلاك حقول التخطيط من تبويب Layout (Stage 4)
    //  • Orientation  : تدوير 90° للوضع الأفقي
    //  • Borderless   : توسيع (bleed) بحواف لأسود الحبر خارج حدود المقاس
    //  • NUp          : تركيب نسخ متكررة في ورقة واحدة (2/4/9/16) — استيكرات
    //  • Duplex       : L1800 بلا وحدة duplex آلية — تنفيذ يدوي (وجهان مع قلب)
    //  • Brochure     : وضع الكتيب ⇒ وجهان بربط الحافة الطويلة
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>نتيجة تطبيق التخطيط على الشبكة</summary>
    public sealed class LayoutResult
    {
        public InkChannels6[,] Grid { get; init; } = new InkChannels6[0, 0];
        public int Width { get; init; }
        public int Height { get; init; }
        /// <summary>سجل القرارات المطبقة (للسجل الحي)</summary>
        public string[] AppliedSteps { get; init; } = Array.Empty<string>();
    }

    public static class LayoutComposer
    {
        /// <summary>
        /// تدوير 90° — للوضع الأفقي (Landscape)
        /// (w,h) → (h,w)
        /// </summary>
        public static LayoutResult Rotate90(InkChannels6[,] grid, int w, int h)
        {
            var outGrid = new InkChannels6[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    outGrid[x, h - 1 - y] = grid[y, x];

            return new LayoutResult
            {
                Grid = outGrid, Width = h, Height = w,
                AppliedSteps = [$"تدوير 90° — الاتجاه الأفقي ({h}×{w})"]
            };
        }

        /// <summary>
        /// قلب أفقي — للوجه الثاني في الطباعة اليدوية (Duplex ربط الحافة الطويلة)
        /// </summary>
        public static LayoutResult MirrorHorizontal(InkChannels6[,] grid, int w, int h)
        {
            var outGrid = new InkChannels6[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    outGrid[y, w - 1 - x] = grid[y, x];

            return new LayoutResult
            {
                Grid = outGrid, Width = w, Height = h,
                AppliedSteps = ["قلب أفقي للوجه الثاني (Duplex يدوي)"]
            };
        }

        /// <summary>
        /// توسيع بلا حواف (Borderless) — نسخ الحافة الخارجية بمقدار bleed px
        /// القاعدة: bleed = 1mm عند 720dpi ≈ 28px لكل جهة
        /// </summary>
        public static LayoutResult PadBleed(InkChannels6[,] grid, int w, int h,
            int resolutionDpi = 720, double bleedMm = 1.0)
        {
            int bleed = Math.Max(1, (int)Math.Round(resolutionDpi * bleedMm / 25.4));
            int nw = w + bleed * 2;
            int nh = h + bleed * 2;
            var outGrid = new InkChannels6[nh, nw];

            for (int y = 0; y < nh; y++)
            {
                int sy = Math.Clamp(y - bleed, 0, h - 1);
                for (int x = 0; x < nw; x++)
                {
                    int sx = Math.Clamp(x - bleed, 0, w - 1);
                    outGrid[y, x] = grid[sy, sx];
                }
            }

            return new LayoutResult
            {
                Grid = outGrid, Width = nw, Height = nh,
                AppliedSteps = [$"بلا حواف: bleed {bleed}px ({bleedMm}mm) → {nw}×{nh}"]
            };
        }

        /// <summary>
        /// تركيب N-up — تكرار الصورة نفسها في خلايا الصفحة (استيكرات)
        /// 1→1 | 2→1×2 | 4→2×2 | 9→3×3 | 16→4×4
        /// </summary>
        public static LayoutResult ComposeNUp(InkChannels6[,] grid, int w, int h, int nUp,
            double marginRatio = 0.02)
        {
            if (nUp <= 1)
                return new LayoutResult
                { Grid = grid, Width = w, Height = h, AppliedSteps = ["N-up: 1 (بدون تغيير)"] };

            (int cols, int rows) = nUp switch
            {
                2  => (2, 1),
                4  => (2, 2),
                9  => (3, 3),
                16 => (4, 4),
                _  => (1, 1)
            };

            int margin = (int)Math.Max(1, Math.Min(w, h) * marginRatio);
            int nw = cols * w + (cols + 1) * margin;
            int nh = rows * h + (rows + 1) * margin;
            var outGrid = new InkChannels6[nh, nw];

            // خلفية بيضاء (لا حبر)
            var blank = new InkChannels6();

            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    outGrid[y, x] = blank;

            // تكرار الصورة في كل خلية
            var steps = new List<string>();
            for (int cell = 0; cell < cols * rows; cell++)
            {
                int cx = cell % cols;
                int cy = cell / cols;
                int ox = margin + cx * (w + margin);
                int oy = margin + cy * (h + margin);

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        outGrid[oy + y, ox + x] = grid[y, x];
            }

            steps.Add($"N-up ×{nUp} ({cols}×{rows}) → {nw}×{nh}px");
            return new LayoutResult
            { Grid = outGrid, Width = nw, Height = nh, AppliedSteps = steps.ToArray() };
        }

        /// <summary>
        /// تطبيق سلسلة التخطيط كاملة حسب PipelineSettings — النقطة الوحيدة من الـ UI
        /// </summary>
        public static LayoutResult Apply(InkChannels6[,] grid, int w, int h, PipelineSettings s)
        {
            var steps = new List<string>();
            var cur = grid;
            int cw = w, chh = h;

            // ① الاتجاه
            if (s.OrientationIndex == 1)
            {
                var r = Rotate90(cur, cw, chh);
                cur = r.Grid; cw = r.Width; chh = r.Height;
                steps.AddRange(r.AppliedSteps);
            }

            // ② بلا حواف
            if (s.Borderless)
            {
                int dpi = ResolutionDpiOf(s);
                var p = PadBleed(cur, cw, chh, dpi);
                cur = p.Grid; cw = p.Width; chh = p.Height;
                steps.AddRange(p.AppliedSteps);
            }

            // ③ N-up
            if (s.NUpPages > 1)
            {
                var n = ComposeNUp(cur, cw, chh, s.NUpPages);
                cur = n.Grid; cw = n.Width; chh = n.Height;
                steps.AddRange(n.AppliedSteps);
            }

            return new LayoutResult
            { Grid = cur, Width = cw, Height = chh, AppliedSteps = steps.ToArray() };
        }

        /// <summary>خريطة الجودة 1-7 → الدقة الفعلية + multi-pass</summary>
        public static (PrintResolution Resolution, bool MultiPass) QualityToResolution(int quality)
        {
            return quality switch
            {
                <= 2 => (PrintResolution.Dpi360,  false),
                <= 4 => (PrintResolution.Dpi720,  false),
                <= 6 => (PrintResolution.Dpi720,  true),
                _    => (PrintResolution.Dpi1440, true)
            };
        }

        private static int ResolutionDpiOf(PipelineSettings s)
            => s.Resolution switch
            {
                PrintResolution.Dpi360  => 360,
                PrintResolution.Dpi720  => 720,
                PrintResolution.Dpi1440 => 1440,
                _                       => 2880
            };
    }
}