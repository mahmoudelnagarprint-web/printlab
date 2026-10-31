using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ImageMagick;

namespace ElnagarPrint.RIP.Core
{
    /// <summary>
    /// إعدادات سير العمل الكامل (يرثها الـ UI من الـ sliders)
    /// </summary>
    public class PipelineSettings
    {
        // ── ColorSeparator gates (0.0 = مطفأ، 1.0 = كامل) ──
        public double CyanGate          { get; set; } = 1.0;
        public double MagentaGate       { get; set; } = 1.0;
        public double YellowGate        { get; set; } = 1.0;
        public double BlackGate         { get; set; } = 1.0;
        public double LightCyanGate     { get; set; } = 1.0;
        public double LightMagentaGate  { get; set; } = 0.0; // ⚠️ إطفاء Lm افتراضياً

        // ── ColorSeparator factors ──
        public double LightCyanFactor   { get; set; } = 0.5;
        public double LightMagentaFactor{ get; set; } = 0.5;

        // ── Halftone settings ──
        public bool UseMultiLevel       { get; set; } = true;
        public double LightThreshold    { get; set; } = 0.45;
        public double DarkThreshold     { get; set; } = 0.50;
        public double YellowThreshold   { get; set; } = 0.48;

        // ── ESC/P2 encoder settings ──
        public PrintResolution Resolution { get; set; } = PrintResolution.Dpi720;
        public InkSet InkSet              { get; set; } = InkSet.CMYKcc;
        public bool UseCompression        { get; set; } = true;
        public bool UseMicroWeave         { get; set; } = true;
        public bool BiDirectional         { get; set; } = true;

        // ── USB settings ──
        public int InterPassDelayMs      { get; set; } = 600;

        // ── Multi-pass toggle ──
        public bool UseMultiPass         { get; set; } = true; // 3-pass droplet sizes

        // ════════════════════════════════════════════════════════════════════
        //  حقول واجهة المستخدم (Stage 3) — تُستهلك في Stage 4 بالكامل
        // ════════════════════════════════════════════════════════════════════

        // ── تبويب Main ──
        /// <summary>مقاس الورق: A4=0,A5=1,A3=2,A3+=3,Letter=4,Legal=5,Roll100=6,Roll210=7,Custom=8</summary>
        public int PaperSizeIndex        { get; set; } = 2;    // A3+ شائع للورشة
        /// <summary>مصدر الورق: RearTray=0, Cassette=1, Roll=2, Manual=3</summary>
        public int PaperSourceIndex      { get; set; } = 0;
        /// <summary>طباعة بلا حواف</summary>
        public bool Borderless           { get; set; } = true;
        /// <summary>مستوى الجودة 1..7 (يحدد الدقة الفعلية والسرعة)</summary>
        public int QualityLevel          { get; set; } = 5;

        // ── ضبط الصورة (تُستهلك من IccColorEngine) ──
        public double Brightness         { get; set; } = 1.0;  // 0.0..2.0
        public double Contrast           { get; set; } = 1.0;
        public double Density            { get; set; } = 1.0;  // كثافة الحبر العامّة

        // ── تبويب Layout ──
        /// <summary>0=عمودي 1=أفقي</summary>
        public int OrientationIndex      { get; set; } = 0;
        /// <summary>Duplex: 0=وجه واحد, 1=حافة طويلة, 2=حافة قصيرة</summary>
        public int DuplexIndex           { get; set; } = 0;
        public int Copies                { get; set; } = 1;
        /// <summary>N-up: 1,2,4,9,16</summary>
        public int NUpPages              { get; set; } = 1;
        public bool Brochure             { get; set; } = false;

        // ── تبويب Color ──
        /// <summary>وضع اللون: 0..5 (Monochrome..FilmTransparent)</summary>
        public int ColorModeIndex        { get; set; } = 3;
        /// <summary>نوايا ICC: 0..6 (Inksave..NoCorrection)</summary>
        public int RenderingIntentIndex  { get; set; } = 2;
        /// <summary>فضاء اللون: 0=sRGB..3=DeviceCMYK</summary>
        public int ColorSpaceIndex       { get; set; } = 0;
        /// <summary>نقطة البياض: 0=D50, 1=D65</summary>
        public int WhitePointIndex       { get; set; } = 1;
        public double LabL               { get; set; } = 0;
        public double LabA               { get; set; } = 0;
        public double LabB               { get; set; } = 0;
        public double Saturation         { get; set; } = 1.0;

        // ── تبويب Correction (0..200%) ──
        public double CorrCyanPercent    { get; set; } = 100;
        public double CorrMagentaPercent { get; set; } = 100;
        public double CorrYellowPercent  { get; set; } = 100;
        public double CorrBlackPercent   { get; set; } = 100;

        // ── تبويب Consumption ──
        public bool ShowInkCost          { get; set; } = true;

        // ── تبويب Ink Ctrl ──
        /// <summary>UCR — مستوى الأسود 0..100</summary>
        public double BlackLevelPercent  { get; set; } = 0;
        /// <summary>الحد الإجمالي 100..400%</summary>
        public double TotalInkLimitPercent { get; set; } = 300;
        public double CyanInkLimitPercent   { get; set; } = 100;
        public double MagentaInkLimitPercent{ get; set; } = 100;
        public double YellowInkLimitPercent { get; set; } = 100;
        public double BlackInkLimitPercent  { get; set; } = 100;

        // ── تبويب Options ──
        /// <summary>عمق المعالجة: 8 أو 16 بت</summary>
        public int BitDepth              { get; set; } = 8;
        /// <summary>قص الرول التلقائي</summary>
        public bool AutoRollCut          { get; set; } = false;
        /// <summary>تفعيل وقفات التجفيف الذكية</summary>
        public bool SmartDryingPause     { get; set; } = true;
        /// <summary>ارتفاع الرأس 0..5</summary>
        public int HeadHeightLevel       { get; set; } = 2;
        /// <summary>تشغيل الـ Weave ضد الـ Banding</summary>
        public bool UseWeave             { get; set; } = true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  ناتج سير العمل الكامل
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ملخص مهمة الطباعة بعد انتهاء الـ pipeline
    /// </summary>
    public class PipelineResult
    {
        public bool     Success       { get; init; }
        public string   Error         { get; init; } = "";
        public TimeSpan TotalTime    { get; init; }
        public int      ImageWidth    { get; init; }
        public int      ImageHeight   { get; init; }
        public int      BytesSent     { get; init; }
        public string   PrinterName   { get; init; } = "";

        /// <summary>الوقت المستغرق في كل مرحلة</summary>
        public TimeSpan ColorSepTime  { get; init; }
        public TimeSpan HalftoneTime  { get; init; }
        public TimeSpan EncodeTime    { get; init; }
        public TimeSpan UsbTime       { get; init; }

        // ── Stage 4 ──
        /// <summary>تقدير استهلاك الحبر والتكلفة لكل نسخة</summary>
        public InkEstimate? InkEstimate { get; init; }
        /// <summary>عدد النسخ المرسلة فعلياً</summary>
        public int CopiesSent { get; init; } = 1;
        /// <summary>مسار النقل المستخدم: USB مباشر أو Windows Spooler</summary>
        public string TransportUsed { get; init; } = "";
        /// <summary>قرارات التخطيط المطبقة (تدوير/bleed/N-up)</summary>
        public string[] LayoutSteps { get; init; } = Array.Empty<string>();
        /// <summary>عدد الأوجه المطبوعة (1 أو 2 في الـ duplex اليدوي)</summary>
        public int SidesPrinted { get; init; } = 1;

        public override string ToString() => Success
            ? $"✅ ناجح — {ImageWidth}×{ImageHeight}px | {BytesSent} bytes | {TotalTime.TotalSeconds:F1}s | طابعة: {PrinterName} | نقل: {TransportUsed}"
            : $"❌ فشل: {Error}";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  نقطة الدخول: RipPipeline — يربط كل الطبقات مع بعض (Stage 4)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// مُنسّق سير العمل (Pipeline) — نقطة الدخول للـ UI
    /// Load → IccColorEngine (فلاتر حقيقية) → InkControlProcessor (UCR/حدود)
    /// → LayoutComposer (تدوير/bleed/N-up) → Halftone → Weave → ESCP2Encoder (VDS + ميكانيكا)
    /// → UsbPrinterPort أو WindowsSpoolerPort × نسخ × أوجه
    /// </summary>
    public class RipPipeline
    {
        private readonly PipelineSettings _settings;

        /// <summary>تقدم مباشر للواجهة: (نسبة 0-100, وصف)</summary>
        public event Action<int, string>? Progress;

        /// <summary>معالج التنقيط (يُنشأ عند الحاجة)</summary>
        private HalftoneProcessor? _halftoner;

        /// <summary>منفذ USB الجاري (يُغلق في finally)</summary>
        private UsbPrinterPort? _usbPort;

        /// <summary>طابور Windows Spooler (fallback) — null يعني USB مباشر</summary>
        private string? _spoolerQueue;

        public RipPipeline() : this(new PipelineSettings()) { }

        public RipPipeline(PipelineSettings settings)
        {
            _settings = settings ?? new PipelineSettings();
        }

        private void Report(int pct, string msg) => Progress?.Invoke(pct, msg);

        // ════════════════════════════════════════════════════════════════════════
        //  المهمة الرئيسية: PrintImage
        // ════════════════════════════════════════════════════════════════════════

        public PipelineResult PrintImage(string imagePath, bool autoOpenPrinter = true,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            Report(2, "بدء مهمة الطباعة…");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // ① تحميل الصورة
                Report(5, "① تحميل الصورة…");
                var (rgb, w, h) = LoadImageToRGB(imagePath);
                if (rgb == null)
                    return Fail($"تعذر تحميل الصورة: {imagePath}", sw);

                // ② المحرك اللوني — الفلاتر الحقيقية
                Report(10, "② المحرك اللوني — فلاتر حقيقية…");
                var sepSw = Stopwatch.StartNew();
                var (inkGrid, inkW, inkH) = ApplyColorEngineAndInkControl(rgb, w, h, cancellationToken);
                sepSw.Stop();
                if (inkGrid == null)
                    return Fail("فشل فصل الألوان", sw);
                Report(30, $"② فصل ألوان + تحكم حبر: {inkW}×{inkH}");

                // ③ التخطيط — تدوير / بلا حواف / N-up
                Report(35, "③ تطبيق التخطيط…");
                var layout = LayoutComposer.Apply(inkGrid, inkW, inkH, _settings);
                Report(42, $"③ التخطيط: {layout.Width}×{layout.Height} — {layout.AppliedSteps.Length} خطوة");

                // ④ تقدير الحبر (من الشبكة قبل التنقيط)
                InkEstimate? estimate = null;
                if (_settings.ShowInkCost)
                {
                    estimate = EstimateFromGrid(inkGrid, inkW, inkH);
                    if (estimate != null)
                        Report(46, $"④ تقدير: {estimate.TotalMilliliters:F1} مل — {estimate.TotalCostEGP:F0} ج.م");
                }

                // ⑤ التنقيط
                Report(50, "⑤ التنقيط (Halftone)…");
                _halftoner = new HalftoneProcessor(new HalftoneSettings
                {
                    UseMultiLevel         = _settings.UseMultiLevel,
                    LightChannelThreshold = _settings.LightThreshold,
                    DarkChannelThreshold  = _settings.DarkThreshold,
                    YellowThreshold       = _settings.YellowThreshold
                });
                var halftSw = Stopwatch.StartNew();
                var halftone = _halftoner.Process6Channels(layout.Grid);
                halftSw.Stop();
                Report(62, $"⑤ التنقيط: {halftone.Width}×{halftone.Height}");

                // ⑥ الـ Weave ضد الـ Banding (ضمن الـ multi-pass)
                if (_settings.UseWeave && _settings.UseMultiPass)
                {
                    Report(64, "⑥ توزيع الـ Weave…");
                    var weaveEngine = new WeaveEngine(WeavePattern.L1800_TriplePass());
                    var map = weaveEngine.BuildNozzleMap(halftone.Width, halftone.Height);
                    var woven = weaveEngine.ApplyWeave(halftone, map);
                    halftone = WeaveToHalftone(woven);
                }

                // ⑦ التشفير — VDS 3 passes + الميكانيكا
                Report(68, "⑦ التشفير ESC/P2…");
                var encSw = Stopwatch.StartNew();
                var mechanic = BuildMechanics();
                // الجودة 1-2 تجبر 360dpi (مسودة سريعة)؛ غير ذلك كومبو الدقة في Options هو الحاكم
                var (_, qualityMultiPass) = LayoutComposer.QualityToResolution(_settings.QualityLevel);
                var multiPass = _settings.UseMultiPass && qualityMultiPass;
                var effectiveRes = _settings.QualityLevel <= 2
                    ? PrintResolution.Dpi360
                    : _settings.Resolution;
                var encoder = new ESCP2Encoder(new EncoderSettings
                {
                    Resolution        = effectiveRes,
                    InkSet            = _settings.InkSet,
                    UseCompression    = _settings.UseCompression,
                    UseMicroWeave     = _settings.UseMicroWeave,
                    BiDirectional     = _settings.BiDirectional,
                    MechanicProfile   = mechanic,
                    IncludeCutCommand = _settings.AutoRollCut
                });

                EncodedPage? single = multiPass ? null : encoder.EncodePage(halftone);
                ESCP2Encoder.VDSEncodedResult? vds = multiPass
                    ? encoder.EncodePageVDS(halftone, null, mechanic)
                    : null;
                encSw.Stop();

                int smartDelay = ComputeSmartInterPassDelay(vds, mechanic);
                Report(76, $"⑦ التشفير: {(vds?.TotalBytes ?? single?.TotalBytes ?? 0):N0} بايت — وقفة ذكية {smartDelay}ms");

                // ⑧ النقل الفعلي — USB مباشر أو Windows Spooler (fallback)
                Report(80, "⑧ الاتصال بالطابعة…");
                var usbSw = Stopwatch.StartNew();
                var transport = OpenTransport(autoOpenPrinter);
                if (!transport.success)
                    return Fail(transport.error, sw);

                string transportKind = transport.kind;
                int totalBytes = 0;
                int sidesPrinted = 1;

                // الوجه الأول × عدد النسخ
                int copies = Math.Max(1, _settings.Copies);
                for (int copy = 1; copy <= copies; copy++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Report(Math.Min(90, 80 + copy * 8 / copies),
                        $"⑧ إرسال نسخة {copy}/{copies} — الوجه 1…");

                    var r = (vds != null)
                        ? SendPasses(vds, smartDelay)
                        : SendPage(single!);

                    if (!r.Success)
                    {
                        usbSw.Stop();
                        return Fail($"فشل الإرسال (نسخة {copy}): {r.Error}", sw,
                            estimate, layout, transportKind, copy, sidesPrinted);
                    }
                    totalBytes += r.BytesSent;

                    // Duplex يدوي — الوجه الثاني (مُعكوس) بعد آخر نسخة
                    if ((_settings.DuplexIndex > 0 || _settings.Brochure) && copy == copies)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Report(93, "⑨ الوجه الثاني — اقلب الورقة…");
                        var side2 = MirrorHalftone(halftone);
                        var single2 = encoder.EncodePage(side2);
                        var r2 = SendPage(single2);
                        if (!r2.Success)
                        {
                            usbSw.Stop();
                            return Fail($"فشل إرسال الوجه الثاني: {r2.Error}", sw,
                                estimate, layout, transportKind, copies, 1);
                        }
                        totalBytes += r2.BytesSent;
                        sidesPrinted = 2;
                    }
                }
                usbSw.Stop();

                sw.Stop();
                Report(100, "✅ اكتملت الطباعة");

                return new PipelineResult
                {
                    Success       = true,
                    TotalTime     = sw.Elapsed,
                    ImageWidth    = halftone.Width,
                    ImageHeight   = halftone.Height,
                    BytesSent     = totalBytes,
                    PrinterName   = transport.name,
                    ColorSepTime  = sepSw.Elapsed,
                    HalftoneTime  = halftSw.Elapsed,
                    EncodeTime    = encSw.Elapsed,
                    UsbTime       = usbSw.Elapsed,
                    InkEstimate   = estimate,
                    CopiesSent    = copies,
                    TransportUsed = transportKind,
                    LayoutSteps   = layout.AppliedSteps,
                    SidesPrinted  = sidesPrinted
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail($"استثناء: {ex.Message}", sw);
            }
            finally
            {
                CloseTransport();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  المعاينة: PreviewHalftone — نفس المسار حتى التنقيط
        // ════════════════════════════════════════════════════════════════════════

        public HalftoneResult PreviewHalftone(string imagePath)
        {
            var (rgb, w, h) = LoadImageToRGB(imagePath);
            if (rgb == null)
                throw new IOException($"تعذر تحميل الصورة: {imagePath}");

            var (inkGrid, inkW, inkH) = ApplyColorEngineAndInkControl(rgb, w, h);
            if (inkGrid == null)
                throw new IOException("فشل فصل الألوان في المعاينة");

            // المعاينة: تدوير فقط — بلا N-up/bleed (للوضوح البصري)
            var preview = new PipelineSettings { OrientationIndex = _settings.OrientationIndex };
            var layout = LayoutComposer.Apply(inkGrid, inkW, inkH, preview);

            _halftoner = new HalftoneProcessor(new HalftoneSettings
            {
                UseMultiLevel         = _settings.UseMultiLevel,
                LightChannelThreshold = _settings.LightThreshold,
                DarkChannelThreshold  = _settings.DarkThreshold,
                YellowThreshold       = _settings.YellowThreshold
            });

            return _halftoner.Process6Channels(layout.Grid);
        }

        /// <summary>
        /// ① تحميل الصورة → مصفوفة RGB double (0..1) — عبر Magick.NET
        /// يدعم: BMP/JPG/PNG/TIFF/PSD/WebP + EXIF orientation + 16-bit حقيقي
        /// (BitDepth من تبويب Options: 8 → 0..255، 16 → 0..65535 بمساحة تدرّج أعلى)
        /// </summary>
        private (double[]?, int, int) LoadImageToRGB(string imagePath)
        {
            if (!File.Exists(imagePath))
                return (null, 0, 0);

            using var image = new MagickImage(imagePath);

            // تصحيح اتجاه EXIF تلقائيًا
            image.AutoOrient();

            // توحيد فضاء اللون — CMYK/Grayscale JPEG تتحول sRGB
            if (image.ColorSpace != ColorSpace.sRGB)
                image.ColorSpace = ColorSpace.sRGB;

            int w = (int)image.Width, h = (int)image.Height;

            // حد أقصى 15MP — تصغير تلقائي (A3+ @720dpi ≈ 11MP)
            const long maxPixels = 15_000_000;
            if ((long)w * h > maxPixels)
            {
                double scale = Math.Sqrt((double)maxPixels / ((double)w * h));
                int nw = Math.Max(1, (int)(w * scale));
                int nh = Math.Max(1, (int)(h * scale));
                image.Resize(new MagickGeometry((uint)nw, (uint)nh));
                w = (int)image.Width; h = (int)image.Height;
            }

            var rgb = new double[w * h * 3];

            // بناء Q16: GetArea يُرجع ushort[] (0..65535) — أعلى دقة ممكنة من المصدر
            // • BitDepth=16 → دقة كاملة عبر الـ pipeline
            // • BitDepth=8  → تكميم لمستويات 256 (محاكاة مسار 8 بت القياسي)
            using var pix = image.GetPixels();
            ushort[] area = pix.GetArea(0, 0, (uint)w, (uint)h);
            int channels = (int)image.ChannelCount;
            int stride = w * channels;
            bool quantize8 = _settings.BitDepth != 16;

            for (int r = 0; r < h; r++)
            {
                int rowBase = r * w * 3;
                for (int c = 0; c < w; c++)
                {
                    int src = r * stride + c * channels;

                    double rr = area[src]     / 65535.0;
                    double gg = area[src + 1] / 65535.0;
                    double bb = area[src + 2] / 65535.0;

                    if (quantize8)
                    {
                        rr = Math.Round(rr * 255.0) / 255.0;
                        gg = Math.Round(gg * 255.0) / 255.0;
                        bb = Math.Round(bb * 255.0) / 255.0;
                    }

                    rgb[rowBase + c * 3 + 0] = rr; // R
                    rgb[rowBase + c * 3 + 1] = gg; // G
                    rgb[rowBase + c * 3 + 2] = bb; // B
                }
            }

            return (rgb, w, h);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ② المحرك اللوني الحقيقي + التحكم بالحبر
        // ════════════════════════════════════════════════════════════════════════

        private (InkChannels6[,]?, int, int) ApplyColorEngineAndInkControl(
            double[]? rgb, int w, int h, CancellationToken cancellationToken = default)
        {
            if (rgb == null) return (null, 0, 0);

            using var engine = new IccColorEngine(MapColorEngineSettings());
            var inkControl = new InkControlProcessor(MapInkControlSettings())
            {
                LcFactor = _settings.LightCyanFactor,
                LmFactor = _settings.LightMagentaFactor
            };

            var grid = new InkChannels6[h, w];
            int lastReport = 10;

            for (int row = 0; row < h; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int rowBase = row * w * 3;
                for (int col = 0; col < w; col++)
                {
                    if ((col & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double r = rgb[rowBase + col * 3 + 0];
                    double g = rgb[rowBase + col * 3 + 1];
                    double b = rgb[rowBase + col * 3 + 2];

                    // ① المحرك اللوني: Intent → فلاتر → LAB → ColorMode → CMYK
                    var px = engine.TransformRGB(r, g, b);

                    // ② التحكم بالحبر: Lc/Lm → تصحيح → UCR → حدود
                    var ink8 = inkControl.Process(px.C, px.M, px.Y, px.K);

                    // ③ الـ Gates — أزرار الإطفاء (Zero Lm/Lc)
                    double lc = Math.Clamp(ink8.Lc * _settings.LightCyanGate, 0, 1);
                    double lm = Math.Clamp(ink8.Lm * _settings.LightMagentaGate, 0, 1);

                    // ④ الكثافة العامّة (Density من تبويب Main)
                    double d = _settings.Density;

                    grid[row, col] = new InkChannels6
                    {
                        Cyan         = Math.Clamp(ink8.C  * d, 0, 1),
                        Magenta      = Math.Clamp(ink8.M  * d, 0, 1),
                        Yellow       = Math.Clamp(ink8.Y  * d, 0, 1),
                        Black        = Math.Clamp(ink8.K  * d, 0, 1),
                        LightCyan    = lc,
                        LightMagenta = lm
                    };
                }

                if (row % 128 == 0)
                {
                    int pct = 10 + row * 18 / Math.Max(1, h);
                    if (pct > lastReport)
                    {
                        lastReport = pct;
                        Report(pct, $"② فصل ألوان: {row * 100 / Math.Max(1, h)}%");
                    }
                }
            }

            return (grid, w, h);
        }

        /// <summary>خريطة إعدادات الواجهة → ColorEngineSettings</summary>
        private ColorEngineSettings MapColorEngineSettings()
        {
            // وضع اللون: 0..5 → Core ColorMode
            var colorMode = _settings.ColorModeIndex switch
            {
                0 => ColorMode.Grayscale,      // Monochrome
                1 => ColorMode.GrayscaleFast,  // GrayscaleRGB
                2 => ColorMode.ColorRGB,       // StandardPhoto
                3 => ColorMode.ColorRGB,       // VividPhoto (+إشباع 15%)
                4 => ColorMode.ColorRGB,       // Sublimation (+كثافة 15%)
                _ => ColorMode.ColorRGB        // FilmTransparent (+كثافة 25%)
            };

            // نوايا ICC: 0..6 → Core RenderingIntent
            var intent = _settings.RenderingIntentIndex switch
            {
                0 => RenderingIntent.PerceptionInksave,   // ⚡ Inksave 20-35%
                1 => RenderingIntent.PerceptionPhoto,
                2 => RenderingIntent.Perception,
                3 => RenderingIntent.Saturation,
                4 => RenderingIntent.ColorimetricAbsolute,
                5 => RenderingIntent.ColorimetricRelative,
                _ => RenderingIntent.NoCorrection
            };

            // الإشباع: VividPhoto يرفعه 15%
            double sat = _settings.Saturation;
            if (_settings.ColorModeIndex == 3) sat = Math.Min(2.0, sat * 1.15);

            // الكثافة: Sublimation +15%، الفيلم +25%
            double density = _settings.Density;
            if (_settings.ColorModeIndex == 4) density = Math.Min(2.0, density * 1.15);
            if (_settings.ColorModeIndex == 5) density = Math.Min(2.0, density * 1.25);

            return new ColorEngineSettings
            {
                ColorMode   = colorMode,
                Intent      = intent,
                RefLight    = _settings.WhitePointIndex == 0
                                  ? ReferenceLight.D50 : ReferenceLight.D65,
                BitDepth    = _settings.BitDepth,
                Brightness  = _settings.Brightness,
                Contrast    = _settings.Contrast,
                Saturation  = sat,
                // محاور LAB: LabL في الإعدادات 0..1 (1.0 = محايد) — من الواجهة ±100
                LabL        = 1.0 + _settings.LabL / 100.0,
                LabA        = _settings.LabA / 100.0,
                LabB        = _settings.LabB / 100.0
            };
        }

        /// <summary>خريطة إعدادات الواجهة → InkControlSettings</summary>
        private InkControlSettings MapInkControlSettings()
        {
            return new InkControlSettings
            {
                // UCR — Black Level من تبويب Ink Ctrl
                BlackLevelUCR   = _settings.BlackLevelPercent,

                // الحد الإجمالي 100-400%
                TotalInkMaximum = _settings.TotalInkLimitPercent,

                // الحدود الفردية
                ChannelLimitC   = _settings.CyanInkLimitPercent,
                ChannelLimitM   = _settings.MagentaInkLimitPercent,
                ChannelLimitY   = _settings.YellowInkLimitPercent,
                ChannelLimitK   = _settings.BlackInkLimitPercent,

                // تصحيح القنوات من تبويب Correction (0-200%)
                UseIndividualCorrection = true,
                CorrectionCyan    = _settings.CorrCyanPercent,
                CorrectionMagenta = _settings.CorrMagentaPercent,
                CorrectionYellow  = _settings.CorrYellowPercent,
                CorrectionBlack   = _settings.CorrBlackPercent,

                // Black Ink Limit — قيمة افتراضية 100% (CMY في الظلال)
                BlackInkLimit = 100.0
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ⑦ الميكانيكا → MechanicProfile
        // ════════════════════════════════════════════════════════════════════════

        private MechanicProfile BuildMechanics()
        {
            var mechanics = new MechanicsSettings
            {
                Direction = _settings.BiDirectional
                    ? PrintDirection.BiDirectional
                    : PrintDirection.UniDirectional,
                HeadHeightLevel = (HeadHeight)Math.Clamp(_settings.HeadHeightLevel, 0, 5),
                RollCutter = new RollCutterSettings
                {
                    Enabled      = _settings.AutoRollCut,
                    AutoCutAtEnd = _settings.AutoRollCut
                },
                DryingPause = new InkDryingPauseSettings
                {
                    DefaultPauseMs     = _settings.InterPassDelayMs,
                    // Smart off → عتبة مستحيلة (2.0) = لا وقفات إضافية
                    TriggerThreshold   = _settings.SmartDryingPause ? 0.70 : 2.0,
                    DensityBoostFactor = 1.5,
                    MaxPauseMs         = Math.Max(_settings.InterPassDelayMs, 800)
                }
            };

            return new MechanicsControlProcessor(mechanics).BuildProfile();
        }

        /// <summary>الوقفة الذكية بين الـ passes — متوسط الوقفات المحسوبة من الـ halftone</summary>
        private int ComputeSmartInterPassDelay(ESCP2Encoder.VDSEncodedResult? vds, MechanicProfile mp)
        {
            if (!_settings.SmartDryingPause)
                return _settings.InterPassDelayMs;

            if (vds == null || vds.DryingPauses.Length == 0)
                return _settings.InterPassDelayMs;

            double sum = 0; int count = 0;
            foreach (int p in vds.DryingPauses)
                if (p > 0) { sum += p; count++; }

            if (count == 0) return _settings.InterPassDelayMs;

            int avg = (int)(sum / count);
            int cap = Math.Max(mp.MaxInterPassDelayMs, _settings.InterPassDelayMs);
            return Math.Min(Math.Max(avg, _settings.InterPassDelayMs), cap);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ⑧ النقل — USB مباشر (LibUsb) مع fallback إلى Windows Spooler
        // ════════════════════════════════════════════════════════════════════════

        private (bool success, string name, string kind, string error) OpenTransport(bool autoOpen)
        {
            if (!autoOpen)
                return (true, "بدون اتصال", "none", "");

            // ① المحاولة الأولى: USB مباشر عبر LibUsbDotNet
            try
            {
                _usbPort = new UsbPrinterPort(new UsbConnectionSettings
                {
                    VendorId  = EpsonUsbIds.VENDOR_ID_L1800,
                    ProductId = EpsonUsbIds.PRODUCT_ID_L1800
                });
                _usbPort.OpenL1800();
                var name = _usbPort.DiscoveredDevice?.Name ?? "Epson L1800";
                Report(84, $"✓ USB مباشر: {name}");
                return (true, name, "USB مباشر (WinUSB)", "");
            }
            catch (PrinterNotFoundException)
            {
                // الجهاز غير موجود في LibUsb — جرّب الـ spooler
            }
            catch (PrinterAccessException)
            {
                // درايفر Epson يحجز الجهاز — جرّب الـ spooler
            }

            // ② fallback: طابور Windows Spooler (درايفر Epson المثبت)
            var queues = WindowsSpoolerPort.FindL1800Queues();
            if (queues.Length == 0)
            {
                // أي طابور Epson آخر كحل أخير
                var all = WindowsSpoolerPort.DiscoverQueues();
                foreach (var q in all)
                    if (q.Name.Contains("L1800", StringComparison.OrdinalIgnoreCase))
                    { queues = new[] { q }; break; }
            }

            if (queues.Length == 0)
                return (false, "", "",
                    "لم يتم العثور على L1800: لا USB مباشر (WinUSB مطلوب عبر Zadig) " +
                    "ولا طابور Windows. وصّل الطابعة وثبّت درايفر Epson.");

            _usbPort = null;      // النقل عبر الـ spooler
            _spoolerQueue = queues[0].Name;
            Report(84, $"✓ Windows Spooler: {_spoolerQueue}");
            return (true, queues[0].Name, "Windows Spooler (RAW)", "");
        }

        /// <summary>إرسال صفحة واحدة — على المسار المهيأ (USB مباشر أو Spooler)</summary>
        private PrintJobResult SendPage(EncodedPage page)
        {
            if (_spoolerQueue != null)
                return WindowsSpoolerPort.SendRaw(_spoolerQueue, page.Data);

            if (_usbPort != null)
                return _usbPort.SendPrintJob(page);

            return new PrintJobResult { Success = false, Error = "لا يوجد مسار نقل مهيأ" };
        }

        /// <summary>إرسال 3 passes مع وقفة ذكية بينها — على المسار المهيأ</summary>
        private PrintJobResult SendPasses(ESCP2Encoder.VDSEncodedResult vds, int delayMs)
        {
            var r1 = SendPage(vds.Pass1);
            if (!r1.Success) return r1;

            if (delayMs > 0) System.Threading.Thread.Sleep(delayMs);

            var r2 = SendPage(vds.Pass2);
            if (!r2.Success) return r2;

            if (delayMs > 0) System.Threading.Thread.Sleep(delayMs);

            var r3 = SendPage(vds.Pass3);
            if (!r3.Success) return r3;

            return new PrintJobResult
            {
                Success = true,
                BytesSent = r1.BytesSent + r2.BytesSent + r3.BytesSent,
                Duration = r1.Duration + r2.Duration + r3.Duration
            };
        }

        private void CloseTransport()
        {
            try { _usbPort?.Dispose(); } catch { }
            _usbPort = null;
            _spoolerQueue = null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  مساعدات
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>WeaveResult → HalftoneResult (للدخول في المُشفِّر)</summary>
        private static HalftoneResult WeaveToHalftone(WeaveResult w)
        {
            static HalftonedChannel Chan(int width, int height, byte[] map)
                => new HalftonedChannel { Width = width, Height = height, DotMap = map };

            int wd = w.Width, ht = w.Height;
            return new HalftoneResult
            {
                Cyan          = Chan(wd, ht, w.CyanDotMap),
                Magenta       = Chan(wd, ht, w.MagentaDotMap),
                Yellow        = Chan(wd, ht, w.YellowDotMap),
                Black         = Chan(wd, ht, w.BlackDotMap),
                LightCyan     = Chan(wd, ht, w.LightCyanDotMap),
                LightMagenta  = Chan(wd, ht, w.LightMagentaDotMap)
            };
        }

        /// <summary>قلب أفقي لخرائط التنقيط — الوجه الثاني (Duplex يدوي)</summary>
        private static HalftoneResult MirrorHalftone(HalftoneResult src)
        {
            int w = src.Width, h = src.Height;

            static HalftonedChannel Mirror(HalftonedChannel ch, int w, int h)
            {
                var map = new byte[w * h];
                for (int r = 0; r < h; r++)
                    for (int c = 0; c < w; c++)
                        map[r * w + c] = ch.DotMap[r * w + (w - 1 - c)];
                return new HalftonedChannel { Width = w, Height = h, DotMap = map };
            }

            return new HalftoneResult
            {
                Cyan         = Mirror(src.Cyan, w, h),
                Magenta      = Mirror(src.Magenta, w, h),
                Yellow       = Mirror(src.Yellow, w, h),
                Black        = Mirror(src.Black, w, h),
                LightCyan    = Mirror(src.LightCyan, w, h),
                LightMagenta = Mirror(src.LightMagenta, w, h)
            };
        }

        /// <summary>تقدير الحبر من مجموع شبكة القنوات — InkCostEstimator.Estimate</summary>
        private static InkEstimate? EstimateFromGrid(InkChannels6[,] grid, int w, int h)
        {
            try
            {
                double tc = 0, tm = 0, ty = 0, tk = 0, tlc = 0, tlm = 0;
                int rows = grid.GetLength(0);
                int cols = grid.GetLength(1);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        var px = grid[r, c];
                        tc += px.Cyan; tm += px.Magenta; ty += px.Yellow;
                        tk += px.Black; tlc += px.LightCyan; tlm += px.LightMagenta;
                    }

                var totals = new InkChannels8
                { C = tc, M = tm, Y = ty, K = tk, Lc = tlc, Lm = tlm };

                return InkCostEstimator.Estimate(totals, w, h);
            }
            catch
            {
                return null;
            }
        }

        private static PipelineResult Fail(string error, Stopwatch sw,
            InkEstimate? estimate = null, LayoutResult? layout = null,
            string transport = "", int copies = 1, int sides = 1)
            => new PipelineResult
            {
                Success       = false,
                Error         = error,
                TotalTime     = sw.Elapsed,
                InkEstimate   = estimate,
                TransportUsed = transport,
                LayoutSteps   = layout?.AppliedSteps ?? Array.Empty<string>(),
                CopiesSent    = copies,
                SidesPrinted  = sides
            };
    }
}
