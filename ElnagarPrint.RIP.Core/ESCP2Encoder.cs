using System;
using System.IO;
using System.Text;

namespace ElnagarPrint.RIP.Core
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  أوامر بروتوكول ESC/P2 الأساسية (Epson Standard Code for Printers)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// رموز أوامر ESC/P2 الثابتة
    /// </summary>
    public static class ESCP2
    {
        public const byte ESC = 0x1B;   // Escape
        public const byte GS  = 0x1D;   // Group Separator

        // ── أوامر التهيئة ──
        /// <summary>ESC @ — تهيئة الطابعة (Reset)</summary>
        public static readonly byte[] CMD_RESET = { ESC, 0x40 };

        /// <summary>ESC (t — اتجاه الطباعة: 0=Uni-dir, 1=Bi-dir</summary>
        public static readonly byte[] CMD_PRINT_DIR = { ESC, 0x28, 0x74, 0x01, 0x00 };

        // ── أوامر الدقة (Resolution) ──
        /// <summary>ESC (U — ضبط الوحدة: 1/360"</summary>
        public static readonly byte[] CMD_UNIT_360 = { ESC, 0x28, 0x55, 0x01, 0x01 };

        /// <summary>ESC (U — ضبط الوحدة: 1/720"</summary>
        public static readonly byte[] CMD_UNIT_720 = { ESC, 0x28, 0x55, 0x01, 0x02 };

        // ── أوامر الـ Raster ──
        /// <summary>GS v 0 — بدء طباعة الـ Raster (6 planes: K,C,M,Y,Lc,Lm)</summary>
        public static readonly byte[] CMD_RASTER_START =
            { GS, 0x76, 0x30, 0x00 };

        /// <summary>GS v 1 — إنهاء طباعة الـ Raster</summary>
        public static readonly byte[] CMD_RASTER_END =
            { GS, 0x76, 0x31 };

        /// <summary>ESC * m — بدء خط raster
        /// m=0: no compress, m=1: TIFF, m=33: auto compression</summary>
        public static readonly byte CMD_RASTER_LINE = 0x2A; // '*'

        /// <summary>ESC . — نهاية بيانات الـ raster للخط</summary>
        public static readonly byte[] CMD_RASTER_EOD = { ESC, 0x2E };

        // ── أوامر تمرير الورق (Paper Feed) ──
        /// <summary>ESC (v — ضبط المسافة العمودية (line gap)</summary>
        public static byte[] CmdSetLineGap(int units360) =>
            new byte[] { ESC, 0x28, 0x76, 0x04,
                    (byte)(units360 & 0xFF),
                    (byte)((units360 >> 8) & 0xFF),
                    (byte)((units360 >> 16) & 0xFF),
                    (byte)((units360 >> 24) & 0xFF) };

        // ── أوامر الـ Page ──
        /// <summary>FF — بداية صفحة جديدة</summary>
        public const byte CMD_FORM_FEED = 0x0C;

        /// <summary>LF — سطر جديد (line feed)</summary>
        public const byte CMD_LINE_FEED = 0x0A;

        // ── أوامر الـ Micro-weave (تداخل الخطوط) ──
        /// <summary>GS ( W 1 — تفعيل الـ uni-directional print</summary>
        public static readonly byte[] CMD_UNIDIR = { GS, 0x28, 0x57, 0x02, 0x01, 0x00 };

        /// <summary>GS ( W 0 — تفعيل الـ bi-directional print</summary>
        public static readonly byte[] CMD_BIDIR = { GS, 0x28, 0x57, 0x02, 0x00, 0x00 };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  إعدادات التشفير
    // ─────────────────────────────────────────────────────────────────────────────

    public enum PrintResolution
    {
        /// <summary>360 × 360 dpi</summary>
        Dpi360  = 360,
        /// <summary>720 × 720 dpi (recommended for L1800)</summary>
        Dpi720  = 720,
        /// <summary>1440 × 720 dpi —Draft</summary>
        Dpi1440 = 1440,
        /// <summary>2880 × 720 dpi —High Speed</summary>
        Dpi2880 = 2880
    }

    public enum InkSet
    {
        /// <summary>4-color: C,M,Y,K</summary>
        CMYK  = 0,
        /// <summary>6-color: C,M,Y,K,Lc,Lm (Light Cyan, Light Magenta)</summary>
        CMYKcc = 1
    }

    public class EncoderSettings
    {
        /// <summary>دقة الطباعة</summary>
        public PrintResolution Resolution { get; set; } = PrintResolution.Dpi720;

        /// <summary>مجموعة الأحبار</summary>
        public InkSet InkSet { get; set; } = InkSet.CMYKcc;

        /// <summary>تفعيل الضغط (RLE / TIFF)</summary>
        public bool UseCompression { get; set; } = true;

        /// <summary>تفعيل Micro-weave (تداخل الخطوط)</summary>
        public bool UseMicroWeave { get; set; } = true;

        /// <summary>اتجاه الطباعة: true=bi-directional, false=uni-directional</summary>
        public bool BiDirectional { get; set; } = true;

        /// <summary>الـ line gap بالوحدات (360 dpi units)</summary>
        public int LineGap { get; set; } = 0x30; // ≈ 1/12"

        /// <summary>استخدام VDS multi-pass (3 passes: large → medium → small)</summary>
        public bool UseVDSPass { get; set; } = false;

        /// <summary>الملف الميكانيكي (يُقرأ من MechanicsControlProcessor)</summary>
        public MechanicProfile? MechanicProfile { get; set; }

        /// <summary>تضمين أمر القطع في نهاية الصفحة</summary>
        public bool IncludeCutCommand { get; set; } = false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  ناتج الترجمة: حزمة البيانات الجاهزة للإرسال للطابعة
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// حزمة صفحة واحدة جاهزة للإرسال عبر USB
    /// </summary>
    public class EncodedPage
    {
        /// <summary>عدد البايتات الكلي للصفحة</summary>
        public int TotalBytes => (int)_data.Length;

        /// <summary>عرض الصورة بالبكسل</summary>
        public int Width  { get; init; }

        /// <summary>ارتفاع الصورة بالبكسل</summary>
        public int Height { get; init; }

        /// <summary>البيانات الثنائية الفعلية</summary>
        public byte[] Data => _data.ToArray();

        private readonly MemoryStream _data;

        public EncodedPage(int width, int height)
        {
            Width  = width;
            Height = height;
            _data  = new MemoryStream(width * height * 6 + 4096); // تقدير مبدئي
        }

        internal void Append(byte[] bytes) => _data.Write(bytes);
        internal void Append(byte b)        => _data.WriteByte(b);

        /// <summary>تصفير الـ stream للكتابة من جديد (للمعاينة)</summary>
        public byte[] ToArray() => _data.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  مُشفّر ESC/P2: الآلة تنتظرها من الوحدة التالية (USB)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// مُشفّر بروتوكول ESC/P2 لطابعات Epson:
    /// • يحوّل HalftoneResult → حزمة ثنائية جاهزة للإرسال للطابعة
    /// • أوامر التهيئة + خطوط الـ Raster + أعلام النهاية
    /// • هيكل مرن للـ Micro-weave وـ Passes متعددة
    /// </summary>
    public class ESCP2Encoder
    {
        private readonly EncoderSettings _settings;

        // ترتيب Planes لأمر GS v 0 : K, C, M, Y, Lc, Lm
        private static readonly ChannelOrder[] PlaneOrderKCMYcc =
            new[] {
                ChannelOrder.Black,
                ChannelOrder.Cyan,
                ChannelOrder.Magenta,
                ChannelOrder.Yellow,
                ChannelOrder.LightCyan,
                ChannelOrder.LightMagenta
            };

        // ترتيب 4 ألوان (CMYK فقط)
        private static readonly ChannelOrder[] PlaneOrderCMYK =
            new[] {
                ChannelOrder.Cyan,
                ChannelOrder.Magenta,
                ChannelOrder.Yellow,
                ChannelOrder.Black
            };

        public ESCP2Encoder() : this(new EncoderSettings()) { }

        public ESCP2Encoder(EncoderSettings settings)
        {
            _settings = settings ?? new EncoderSettings();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  API الرئيسي: ترميز صفحة كاملة
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ترميز صفحة كاملة من ناتج التنقيط → EncodedPage
        /// </summary>
        public EncodedPage EncodePage(HalftoneResult halftoneResult)
        {
            var page = new EncodedPage(halftoneResult.Width, halftoneResult.Height);

            // ── Phase 1: أوامر التهيئة (Initialization) ──
            AppendInitCommandsPartial(page);

            // ── Phase 2: بدء طباعة الصفحة ──
            AppendPageStart(page, halftoneResult.Height);

            // ── Phase 3: خطوط الـ Raster ──
            var planes = _settings.InkSet == InkSet.CMYKcc
                ? PlaneOrderKCMYcc : PlaneOrderCMYK;

            for (int row = 0; row < halftoneResult.Height; row++)
            {
                // Micro-weave: لو مُفعّل، نغيّر اتجاه الخط
                if (_settings.UseMicroWeave && row % 2 == 1)
                    page.Append(ESCP2.CMD_UNIDIR);

                AppendRasterLine(page, halftoneResult, planes, row);

                if (_settings.UseMicroWeave && row % 2 == 1)
                    page.Append(ESCP2.CMD_BIDIR);
            }

            // ── Phase 4: إنهاء الصفحة ──
            AppendPageEnd(page);

            return page;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Phase 1: أوامر التهيئة (partial — تُكمِل VDS section الكاملة)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// تهيئة basic — لا تُفعّل thin-paper mode.
        /// يُستخدم داخلياً من EncodePage.
        /// للـ VDS والـ thin-paper mode: استخدم AppendInitCommands() من VDS section.
        /// </summary>
        private void AppendInitCommandsPartial(EncodedPage page)
        {
            // ① Reset
            page.Append(ESCP2.CMD_RESET);
            // ② Set print direction (bi/uni)
            var dirCmd = _settings.BiDirectional
                ? ESCP2.CMD_BIDIR : ESCP2.CMD_UNIDIR;
            page.Append(dirCmd);
            // ③ Set resolution unit
            switch (_settings.Resolution)
            {
                case PrintResolution.Dpi360:
                    page.Append(ESCP2.CMD_UNIT_360); break;
                case PrintResolution.Dpi720:
                case PrintResolution.Dpi1440:
                case PrintResolution.Dpi2880:
                    page.Append(ESCP2.CMD_UNIT_720); break;
            }
            // ④ Line gap
            page.Append(ESCP2.CmdSetLineGap(_settings.LineGap));
            // ⑤ Select ink set (6-color for L1800)
            AppendSelectInkSet(page);
            // ⑥ Thin paper mode من MechanicProfile (إن وُجد)
            if (_settings.MechanicProfile?.ThinPaperMode == true)
                page.Append([ESCP2.ESC, 0x28, 0x76, 0x01, 0x04]);
        }

        private void AppendSelectInkSet(EncodedPage page)
        {
            // ESC (K — Select ink type / ink set
            // 0 = CMYK, 1 = CMYK + LcLm (6-color)
            byte inkSelect = (byte)((int)_settings.InkSet & 0xFF);
            var cmd = new byte[] { ESCP2.ESC, 0x28, 0x4B, 0x01, inkSelect };
            page.Append(cmd);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Phase 2: بدء صفحة جديدة
        // ════════════════════════════════════════════════════════════════════════

        private void AppendPageStart(EncodedPage page, int heightInDots)
        {
            // GS v 0 — بدء Raster في 6 planes
            page.Append(ESCP2.CMD_RASTER_START);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Phase 3: ترميز خط raster واحد
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ترميز خط واحد: يمر على الـ 6 planes ويرسل كل plane كخط مستقل
        /// </summary>
        private void AppendRasterLine(
            EncodedPage page,
            HalftoneResult halftone,
            ChannelOrder[] planes,
            int row)
        {
            int width = halftone.Width;

            // نرسل كل plane على حدة (K → C → M → Y → Lc → Lm)
            foreach (var ch in planes)
            {
                var channel = GetChannel(halftone, ch);
                if (channel == null) continue;

                // بدء خط الـ raster لهذا الـ plane
                // ESC * m xL xH [data]
                // m = 0 (no compress) أو 33 (auto compress)
                byte compressMode = _settings.UseCompression ? (byte)33 : (byte)0;
                int byteCount = width; // 1 bit per pixel → byteCount = width

                page.Append(ESCP2.ESC);
                page.Append(ESCP2.CMD_RASTER_LINE);
                page.Append(compressMode);
                page.Append((byte)(byteCount & 0xFF));         // xL (low)
                page.Append((byte)((byteCount >> 8) & 0xFF));  // xH (high)

                // البيانات: 1 bit per pixel per channel
                AppendRasterLineData(page, channel, row, width);
            }
        }

        /// <summary>
        /// تحويل صف من DotMap إلى بايتات مضغوطة (1 bit per pixel)
        /// • 0 = لا قطرة
        /// • 1 = قطرة (أي حجم — الطابعة تطبع حسب pass الحالي)
        ///
        /// RLE v2 — الضغط يُفعّل إذا UseCompression=true:
        ///   • كل 8 بكسل في byte واحد (MSB first)
        ///   • ثم RLE byte-pair: 3+ consecutive same bytes → [0xFF, count, value]
        /// </summary>
        private void AppendRasterLineData(EncodedPage page,
            HalftonedChannel channel, int row, int width)
        {
            if (_settings.UseCompression)
            {
                // Phase A: pack bits
                var bits = new byte[(width + 7) / 8];
                for (int col = 0; col < width; col++)
                {
                    byte dotLevel = channel.GetDot(row, col);
                    if (dotLevel > 0)
                        bits[col / 8] |= (byte)(0x80 >> (col & 7));
                }

                // Phase B: RLE compress + write
                int pos = 0;
                while (pos < bits.Length)
                {
                    var (rleToken, consumed) = RLECompress(bits, pos);
                    page.Append(rleToken);
                    pos += consumed;
                }
            }
            else
            {
                // Uncompressed: pack bits directly without RLE
                int bitAccum = 0;
                int bitCount = 0;
                for (int col = 0; col < width; col++)
                {
                    byte dotLevel = channel.GetDot(row, col);
                    bitAccum = (bitAccum << 1) | (dotLevel > 0 ? 1 : 0);
                    bitCount++;

                    if (bitCount == 8)
                    {
                        page.Append((byte)bitAccum);
                        bitAccum = 0;
                        bitCount = 0;
                    }
                }
                // Padding
                if (bitCount > 0)
                {
                    bitAccum <<= (8 - bitCount);
                    page.Append((byte)bitAccum);
                }
            }

            // Line terminator
            page.Append(ESCP2.CMD_LINE_FEED);
        }

        /// <summary>
        /// RLE Compression v2 — Byte-Pair Encoding:
        /// Searches for runs of 3+ identical consecutive bytes.
        /// Format: [value] repeated → [0xFF, count, value]
        /// Special tokens: 0xFF as literal → [0xFF, 0x01, 0xFF]
        /// Returns (token bytes, bytes consumed from input).
        /// </summary>
        private static (byte[] Token, int Consumed) RLECompress(byte[] data, int start)
        {
            if (start >= data.Length) return ([], 1);

            // Count run of same value starting at `start`
            byte value = data[start];
            int run = 1;
            while (start + run < data.Length && data[start + run] == value && run < 255)
                run++;

            if (run >= 3 && value != 0xFF)
            {
                // Encode as RLE token: [FF, count, value]
                return ([0xFF, (byte)run, value], run);
            }
            else if (value == 0xFF)
            {
                // 0xFF as literal: [FF, 01, FF]
                return ([0xFF, 0x01, 0xFF], 1);
            }
            else
            {
                // Single byte literal
                return ([value], 1);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Phase 4: إنهاء الصفحة
        // ════════════════════════════════════════════════════════════════════════

        private void AppendPageEnd(EncodedPage page)
        {
            // إنهاء الـ Raster
            page.Append(ESCP2.CMD_RASTER_END);
            // سطر جديد + صفحة جديدة
            page.Append(ESCP2.CMD_LINE_FEED);
            page.Append(ESCP2.CMD_FORM_FEED);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  تحويل مستويات النقاط → Multi-pass (Variable Droplet Sizes)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// VDS Multi-Pass Encoder — 3-pass مع وقفات تجفيف ذكية بين التمريرات
        /// Pass 1: Large dots (10 pl) → Pass 2: Medium dots (6 pl) → Pass 3: Small dots (3 pl)
        ///
        /// يُرجع 3 صفحات منفصلة + array وقفات التجفيف بينها
        /// الـ USB layer يرسل بالترتيب: pass1 → [pause1] → pass2 → [pause2] → pass3
        /// </summary>
        /// <param name="halftoneResult">ناتج التنقيط</param>
        /// <param name="dryingPauses">مصفوفة وقفات (باليد من DryingPauseCalculator) — يمرر null لتقييم تلقائي</param>
        /// <param name="mechanicProfile">ملف الميكانيكا — يُستخدم لاستخراج الـ inter-pass delay</param>
        public VDSEncodedResult EncodePageVDS(
            HalftoneResult halftoneResult,
            int[]? dryingPauses = null,
            MechanicProfile? mechanicProfile = null)
        {
            var mp = mechanicProfile ?? _settings.MechanicProfile ?? new MechanicProfile();

            // تقييم وقفات التجفيف تلقائياً لو لم تُمرَّر
            dryingPauses ??= DryingPauseCalculator.ComputeAllPauseMs(
                halftoneResult,
                mp.InterPassDelayMs);

            var pass1 = EncodeVDSFiltered(halftoneResult, 3, mp); // Large only
            var pass2 = EncodeVDSFiltered(halftoneResult, 2, mp); // Medium+
            var pass3 = EncodeVDSFiltered(halftoneResult, 1, mp); // Small+

            return new VDSEncodedResult(pass1, pass2, pass3, dryingPauses);
        }

        private EncodedPage EncodeVDSFiltered(
            HalftoneResult halftoneResult,
            int minLevel,
            MechanicProfile mp)
        {
            var page = new EncodedPage(halftoneResult.Width, halftoneResult.Height);

            // أوامر التهيئة الكاملة
            AppendInitCommands(page);
            AppendPageStart(page, halftoneResult.Height);

            // تطبيق ملف الميكانيكا (ارتفاع الرأس + الاتجاه)
            if (mp.DirectionCommand.Length > 0)  page.Append(mp.DirectionCommand);
            if (mp.HeadHeightCommand.Length > 0) page.Append(mp.HeadHeightCommand);
            if (mp.PaperFeedCommand.Length > 0)  page.Append(mp.PaperFeedCommand);

            var planes = _settings.InkSet == InkSet.CMYKcc
                ? PlaneOrderKCMYcc : PlaneOrderCMYK;

            for (int row = 0; row < halftoneResult.Height; row++)
            {
                if (mp.PrintDirection == PrintDirection.UniDirectional)
                    page.Append(ESCP2.CMD_UNIDIR);

                AppendRasterLineFilteredVDS(page, halftoneResult, planes, row, minLevel);

                if (mp.PrintDirection == PrintDirection.UniDirectional)
                    page.Append(ESCP2.CMD_BIDIR);
            }

            // قطع الورق لو مُفعّل
            if (_settings.IncludeCutCommand && mp.CutterCommand.Length > 0)
                page.Append(mp.CutterCommand);

            AppendPageEnd(page);
            return page;
        }

        /// <summary>
        /// ناتج VDS — 3 passes + وقفات التجفيف
        /// </summary>
        public sealed class VDSEncodedResult
        {
            public EncodedPage Pass1 { get; }
            public EncodedPage Pass2 { get; }
            public EncodedPage Pass3 { get; }
            /// <summary>وقفات التجفيف بين الـ passes — [0] قبل pass2، [1] قبل pass3</summary>
            public int[] DryingPauses { get; }

            public VDSEncodedResult(EncodedPage p1, EncodedPage p2, EncodedPage p3, int[] pauses)
                => (Pass1, Pass2, Pass3, DryingPauses) = (p1, p2, p3, pauses);

            /// <summary>إجمالي حجم البيانات بالبايت</summary>
            public int TotalBytes => Pass1.TotalBytes + Pass2.TotalBytes + Pass3.TotalBytes;
        }

        // ─── نفس الـ bug fix لـ AppendRasterLineDataFiltered ─────────────────────────────────

        private void AppendRasterLineDataFiltered(
            EncodedPage page,
            HalftonedChannel channel,
            int row, int width,
            int minLevel)
        {
            if (_settings.UseCompression)
            {
                var bits = new byte[(width + 7) / 8];
                for (int col = 0; col < width; col++)
                {
                    byte dotLevel = channel.GetDot(row, col);
                    if (dotLevel >= minLevel)
                        bits[col / 8] |= (byte)(0x80 >> (col & 7));
                }

                int pos = 0;
                while (pos < bits.Length)
                {
                    var (token, consumed) = RLECompress(bits, pos);
                    page.Append(token);
                    pos += consumed;
                }
            }
            else
            {
                // Uncompressed bit packing with correct accumulator
                int bitAccum = 0;
                int bitCount = 0;
                for (int col = 0; col < width; col++)
                {
                    byte dotLevel = channel.GetDot(row, col);
                    bitAccum = (bitAccum << 1) | (dotLevel >= minLevel ? 1 : 0);
                    bitCount++;

                    if (bitCount == 8)
                    {
                        page.Append((byte)bitAccum);
                        bitAccum = 0;
                        bitCount = 0;
                    }
                }
                if (bitCount > 0)
                {
                    bitAccum <<= (8 - bitCount);
                    page.Append((byte)bitAccum);
                }
            }

            page.Append(ESCP2.CMD_LINE_FEED);
        }

        private void AppendRasterLineFilteredVDS(
            EncodedPage page,
            HalftoneResult halftone,
            ChannelOrder[] planes,
            int row,
            int minLevel)
        {
            int width = halftone.Width;
            byte compressMode = _settings.UseCompression ? (byte)33 : (byte)0;

            foreach (var ch in planes)
            {
                var channel = GetChannel(halftone, ch);
                if (channel == null) continue;

                page.Append(ESCP2.ESC);
                page.Append(ESCP2.CMD_RASTER_LINE);
                page.Append(compressMode);
                page.Append((byte)(width & 0xFF));
                page.Append((byte)((width >> 8) & 0xFF));

                AppendRasterLineDataFiltered(page, channel, row, width, minLevel);
            }
        }

        // ─── أوامر تهيئة إضافية كاملة لـ L1800 ─────────────────────────────────

        private void AppendInitCommands(EncodedPage page)
        {
            // ① تهيئة الطابعة — Reset
            page.Append(ESCP2.CMD_RESET);

            // ② GS ( W — ضبط اتجاه الطباعة (Bi/Uni)
            var dirCmd = _settings.BiDirectional
                ? ESCP2.CMD_BIDIR : ESCP2.CMD_UNIDIR;
            page.Append(dirCmd);

            // ③ ESC ( U — ضبط الوحدة: 1/360" أو 1/720"
            switch (_settings.Resolution)
            {
                case PrintResolution.Dpi360:
                    page.Append(ESCP2.CMD_UNIT_360); break;
                default:
                    page.Append(ESCP2.CMD_UNIT_720); break;
            }

            // ④ Line gap — المسافة بين_lines
            page.Append(ESCP2.CmdSetLineGap(_settings.LineGap));

            // ⑤ اختيار مجموعة الأحبار — 6 ألوان لـ L1800
            AppendSelectInkSet(page);

            // ⑥ أمر تغذية الورق المحسّنة ( Thin Paper Mode)
            if (_settings.MechanicProfile?.ThinPaperMode == true)
            {
                // ESC ( v — ضبط سرعة التغذية
                page.Append([ESCP2.ESC, 0x28, 0x76, 0x01, 0x04]); // slow feed
            }
        }

        /// <summary>
        /// استرجاع خريطة النقاط لقناة بالأ اسم (public API)
        /// </summary>
        public static byte[] GetChannelDotMap(HalftoneResult result, ChannelOrder order)
            => GetChannel(result, order)?.DotMap ?? [];

        // ─── أدوات مساعدة ─────────────────────────────────

        private static HalftonedChannel GetChannel(HalftoneResult result, ChannelOrder order)
            => order switch
            {
                ChannelOrder.Cyan          => result.Cyan,
                ChannelOrder.Magenta       => result.Magenta,
                ChannelOrder.Yellow        => result.Yellow,
                ChannelOrder.Black         => result.Black,
                ChannelOrder.LightCyan     => result.LightCyan,
                ChannelOrder.LightMagenta  => result.LightMagenta,
                _ => null
            };

        public enum ChannelOrder
        {
            Cyan, Magenta, Yellow, Black, LightCyan, LightMagenta
        }
    }
}
