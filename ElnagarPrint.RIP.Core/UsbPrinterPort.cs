using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace ElnagarPrint.RIP.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  معرّفات USB لطابعات Epson المدعومة
    // ═══════════════════════════════════════════════════════════════════════════

    public static class EpsonUsbIds
    {
        public const ushort VENDOR_ID_EPSON  = 0x04B8;
        public const ushort VENDOR_ID_L1800  = 0x04B8;
        public const ushort PRODUCT_ID_L1800 = 0x1129;

        /// <summary>قائمة الطابعات المدعومة (vid, pid, الاسم)</summary>
        public static readonly (ushort Vid, ushort Pid, string Name)[] SupportedPrinters =
        {
            (0x04B8, 0x1129, "Epson L1800"),
            (0x04B8, 0x1102, "Epson L800"),
            (0x04B8, 0x1103, "Epson L801"),
            (0x04B8, 0x1104, "Epson L805"),
            (0x04B8, 0x1105, "Epson L810"),
            (0x04B8, 0x1106, "Epson L850"),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  إعدادات الاتصال USB
    // ═══════════════════════════════════════════════════════════════════════════

    public class UsbConnectionSettings
    {
        public ushort VendorId      { get; set; } = EpsonUsbIds.VENDOR_ID_L1800;
        public ushort ProductId     { get; set; } = EpsonUsbIds.PRODUCT_ID_L1800;
        /// <summary>رقم الـ interface (L1800 = 0)</summary>
        public int    InterfaceIndex{ get; set; } = 0;
        /// <summary>حجم الـ chunk للكتابة (USB Bulk موصى به 16-64KB)</summary>
        public int    WriteChunkSize{ get; set; } = 32 * 1024;
        /// <summary>timeout الكتابة (ms)</summary>
        public int    TimeoutMs     { get; set; } = 10_000;
        /// <summary>timeout القراءة (ms) — لاستعلام الحالة</summary>
        public int    ReadTimeoutMs { get; set; } = 1_000;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  الطابعة المكتشفة
    // ═══════════════════════════════════════════════════════════════════════════

    public class DiscoveredPrinter
    {
        public string Name    { get; init; } = "";
        public ushort Vid     { get; init; }
        public ushort Pid     { get; init; }
        public string Serial  { get; init; } = "";
        public int    BusNumber { get; init; }
        public int    Address { get; init; }
        public override string ToString() => $"{Name} (VID={Vid:X4} PID={Pid:X4})";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  نتيجة مهمة الطباعة
    // ═══════════════════════════════════════════════════════════════════════════

    public class PrintJobResult
    {
        public bool     Success   { get; init; }
        public string   Error     { get; init; } = "";
        public int      BytesSent { get; init; }
        public TimeSpan Duration  { get; init; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  استثناءات الطباعة
    // ═══════════════════════════════════════════════════════════════════════════

    public class PrinterNotFoundException    : Exception { public PrinterNotFoundException(string m) : base(m) { } }
    public class PrinterAccessException      : Exception { public PrinterAccessException(string m) : base(m) { } }
    public class PrinterConnectionException  : Exception { public PrinterConnectionException(string m) : base(m) { } }
    public class PrinterNotConnectedException: Exception { public PrinterNotConnectedException(string m) : base(m) { } }
    public class PrinterWriteException       : Exception { public PrinterWriteException(string m) : base(m) { } }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UsbPrinterPort — الاتصال المباشر عبر LibUsbDotNet (WinUSB)
    //  ملاحظة تشغيلية مهمة: على Windows يجب أن يكون للجهاز درايفر WinUSB
    //  (zadig أو filter driver) — وإلا يفشل الفتح ويُستخدم WindowsSpoolerPort
    //  كنقل بديل عبر درايفر Epson المثبت تلقائيًا.
    // ═══════════════════════════════════════════════════════════════════════════

    public class UsbPrinterPort : IDisposable
    {
        private readonly UsbConnectionSettings _settings;
        private UsbDevice?  _device;
        private UsbEndpointWriter _writer;
        private UsbEndpointReader _reader;
        private bool _disposed;

        public UsbPrinterPort() : this(new UsbConnectionSettings()) { }

        public UsbPrinterPort(UsbConnectionSettings settings)
        {
            _settings = settings ?? new UsbConnectionSettings();
        }

        /// <summary>الطابعة المكتشفة والمفتوحة حاليًا</summary>
        public DiscoveredPrinter? DiscoveredDevice { get; private set; }

        /// <summary>هل الاتصال مفتوح؟</summary>
        public bool IsOpen => _device?.IsOpen == true;

        // ════════════════════════════════════════════════════════════════════════
        //  ① الاكتشاف — فحص الـ registry بدون فتح الجهاز
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// اكتشف الطابعات المطابقة لـ VID/PID في قائمة LibUsb registry
        /// (فحص خفيف — لا يفتح الجهاز ولا يتصادم مع درايفر Epson)
        /// </summary>
        public static DiscoveredPrinter[] DiscoverPrinters(
            ushort? vid = EpsonUsbIds.VENDOR_ID_EPSON,
            ushort? pid = null)
        {
            var found = new List<DiscoveredPrinter>();

            try
            {
                foreach (UsbRegistry reg in UsbDevice.AllDevices)
                {
                    if (vid.HasValue && reg.Vid != vid.Value) continue;
                    if (pid.HasValue && reg.Pid != pid.Value) continue;

                    found.Add(new DiscoveredPrinter
                    {
                        Name      = IdentifyPrinterName((ushort)reg.Vid, (ushort)reg.Pid, ""),
                        Vid       = (ushort)reg.Vid,
                        Pid       = (ushort)reg.Pid,
                        Serial    = "",
                        BusNumber = 0,
                        Address   = 0
                    });
                }
            }
            catch
            {
                // LibUsb غير متاح (درايفر مفقود) — نرجع قائمة فارغة
                // والـ pipeline يتحول تلقائيًا لـ WindowsSpoolerPort
            }

            return found.ToArray();
        }

        /// <summary>اكتشف طابعات Epson L1800 فقط</summary>
        public static DiscoveredPrinter[] DiscoverL1800()
            => DiscoverPrinters(EpsonUsbIds.VENDOR_ID_L1800, EpsonUsbIds.PRODUCT_ID_L1800);

        /// <summary>اكتشف أي طابعة Epson مدعومة</summary>
        public static DiscoveredPrinter[] DiscoverSupported()
        {
            var results = new List<DiscoveredPrinter>();
            foreach (var (v, p, _) in EpsonUsbIds.SupportedPrinters)
                results.AddRange(DiscoverPrinters(v, p));
            return results.ToArray();
        }

        private static string IdentifyPrinterName(ushort vid, ushort pid, string devicePath)
        {
            foreach (var (v, p, name) in EpsonUsbIds.SupportedPrinters)
                if (v == vid && p == pid) return name;
            return $"Epson ({vid:X4}:{pid:X4})";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ② الفتح — النمط القياسي: UsbDeviceFinder + ClaimInterface
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// افتح اتصال USB مع الطابعة — النمط القياسي LibUsbDotNet
        /// </summary>
        public void Open(ushort? vid = null, ushort? pid = null)
        {
            ushort v = vid  ?? _settings.VendorId;
            ushort p = pid  ?? _settings.ProductId;

            // ① ابحث بالـ finder القياسي
            var finder = new UsbDeviceFinder(v, p);
            _device = UsbDevice.OpenUsbDevice(finder);

            if (_device == null)
                throw new PrinterNotFoundException(
                    $"لم يتم العثور على طابعة USB (VID={v:X4}, PID={p:X4}). " +
                    "تأكد من التوصيل، أو أن درايفر Epson يحجز الجهاز — " +
                    "استخدم WindowsSpoolerPort في هذه الحالة.");

            // ② claim الـ interface (إلزامي على Windows WinUSB)
            if (_device is IUsbDevice wholeUsbDevice)
            {
                try
                {
                    wholeUsbDevice.SetConfiguration(1);
                    wholeUsbDevice.ClaimInterface(_settings.InterfaceIndex);
                }
                catch (Exception ex)
                {
                    _device.Close();
                    _device = null;
                    throw new PrinterAccessException(
                        $"تعذر claim الـ interface: {ex.Message}. " +
                        "قد يحجز درايفر Epson الجهاز — استخدم النقل عبر Windows Spooler.");
                }
            }

            if (!_device.IsOpen)
                throw new PrinterConnectionException("الجهاز مكتشف لكن الاتصال لم يُفتح.");

            // ③ افتح نقاط النهاية — L1800: bulk OUT = 0x01، bulk IN = 0x81
            _writer = _device.OpenEndpointWriter(WriteEndpointID.Ep01, EndpointType.Bulk);
            _reader = _device.OpenEndpointReader(ReadEndpointID.Ep01,
                _settings.WriteChunkSize, EndpointType.Bulk);

            DiscoveredDevice = new DiscoveredPrinter
            {
                Name      = IdentifyPrinterName(v, p, ""),
                Vid       = v,
                Pid       = p,
                Serial    = "",
                BusNumber = 0,
                Address   = 0
            };
        }

        /// <summary>افتح أول L1800 موجودة</summary>
        public void OpenL1800()
        {
            var found = DiscoverL1800();
            if (found.Length == 0 && WindowsSpoolerPort.FindL1800Queues().Length == 0)
                throw new PrinterNotFoundException(
                    "لم يتم العثور على Epson L1800 (لا USB مباشر ولا طابور Windows). " +
                    "تأكد من التوصيل وتشغيل الطابعة.");

            Open(EpsonUsbIds.VENDOR_ID_L1800, EpsonUsbIds.PRODUCT_ID_L1800);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ③ الإرسال — Bulk Transfer على دفعات
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// إرسال صفحة مُشفّرة واحدة (EncodedPage) عبر Bulk Transfer
        /// </summary>
        public PrintJobResult SendPrintJob(EncodedPage page)
        {
            if (!IsOpen)
                throw new PrinterNotConnectedException(
                    "الاتصال مقطوع. اتصل بالطابعة أولاً بـ Open().");

            if (page == null || page.Data.Length == 0)
                return new PrintJobResult { Success = true, BytesSent = 0, Duration = TimeSpan.Zero };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int totalSent = 0;

            try
            {
                byte[] data = page.Data;
                int chunkSize = _settings.WriteChunkSize;
                int offset = 0;

                while (offset < data.Length)
                {
                    int count = Math.Min(chunkSize, data.Length - offset);
                    int sent;

                    ErrorCode ec = _writer.Write(data, offset, count, _settings.TimeoutMs, out sent);

                    if (ec != ErrorCode.Ok)
                        throw new PrinterWriteException($"خطأ في الكتابة على USB: {ec}");

                    totalSent += sent;
                    offset    += sent;

                    if (offset < data.Length)
                        Thread.Sleep(1);
                }

                sw.Stop();
                return new PrintJobResult
                { Success = true, BytesSent = totalSent, Duration = sw.Elapsed };
            }
            catch (LibUsbDotNet.Main.UsbException ex)
            {
                sw.Stop();
                return new PrintJobResult
                { Success = false, Error = $"خطأ USB: {ex.Message}", BytesSent = totalSent, Duration = sw.Elapsed };
            }
        }

        /// <summary>
        /// إرسال عدة صفحات (multi-pass) مع تأخير بينها لتجفيف الحبر
        /// </summary>
        public PrintJobResult SendMultiPassJob(
            EncodedPage pass1,
            EncodedPage pass2,
            EncodedPage pass3,
            int interPassDelayMs = 500)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int totalBytes = 0;

            var r1 = SendPrintJob(pass1);
            if (!r1.Success) return r1;
            totalBytes += r1.BytesSent;

            if (interPassDelayMs > 0) Thread.Sleep(interPassDelayMs);

            var r2 = SendPrintJob(pass2);
            if (!r2.Success) return r2;
            totalBytes += r2.BytesSent;

            if (interPassDelayMs > 0) Thread.Sleep(interPassDelayMs);

            var r3 = SendPrintJob(pass3);
            if (!r3.Success) return r3;
            totalBytes += r3.BytesSent;

            sw.Stop();
            return new PrintJobResult
            { Success = true, BytesSent = totalBytes, Duration = sw.Elapsed };
        }

        /// <summary>
        /// قراءة بايتات حالة من الطابعة (اختياري — بعض الطابعات لا تدعمه)
        /// </summary>
        public byte[] ReadStatus(int maxBytes = 64)
        {
            if (!IsOpen) return Array.Empty<byte>();
            var buf = new byte[maxBytes];
            try
            {
                var ec = _reader.Read(buf, _settings.ReadTimeoutMs, out int read);
                if (ec != ErrorCode.Ok || read == 0) return Array.Empty<byte>();
                return buf[..read];
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ④ الإغلاق
        // ════════════════════════════════════════════════════════════════════════

        public void Close()
        {
            if (_device != null)
            {
                if (_device is IUsbDevice whole)
                {
                    try { whole.ReleaseInterface(_settings.InterfaceIndex); } catch { }
                }
                try { _device.Close(); } catch { }
                _device = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Close();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}