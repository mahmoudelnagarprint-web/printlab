using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ElnagarPrint.RIP.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  WindowsSpoolerPort — نقل بديل عملي عبر Windows Print Spooler
    //  عندما يكون درايفر Epson الرسمي مثبتًا، لا يملك LibUsbDotNet صلاحية الوصول
    //  المباشر للجهاز (الدرايفر يحجزه). الحل العملي المعتمد في الورش:
    //  إرسال RAW (datatype="RAW") إلى طابور الـ spooler — يمر بلا تفسير للطابعة.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>طابعة Windows مكتشفة (اسم الطابور + المنفذ)</summary>
    public readonly record struct SpoolerPrinter(string Name, string Port, string Driver);

    /// <summary>طبقة Windows Spooler — إرسال ESC/P2 خام عبر طابور مثبت</summary>
    public static class WindowsSpoolerPort
    {
        private const uint PRINTER_ENUM_LOCAL      = 0x00000002;
        private const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

        // ── P/Invoke — winspool.drv ──

        [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinterW(string pPrinterName, out IntPtr hPrinter, IntPtr pDefault);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinterW(IntPtr hPrinter, int level, ref DOC_INFO_1W docInfo);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool EnumPrintersW(uint flags, string? name, int level,
            IntPtr pPrinterEnum, int cbBuf, out int pcbNeeded, out int pcReturned);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOC_INFO_1W
        {
            public string pDocName;
            public string pOutputFile;
            public string pDatatype;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PRINTER_INFO_2W
        {
            public string pServerName;
            public string pPrinterName;
            public string pShareName;
            public string pPortName;
            public string pDriverName;
            public string pComment;
            public string pLocation;
            public IntPtr pDevMode;
            public string pSepFile;
            public string pPrintProcessor;
            public string pDatatype;
            public string pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        // ── اكتشاف الطوابير ──

        /// <summary>
        /// اكتشف كل طوابير الطباعة المثبتة (محلية + متصلة)
        /// </summary>
        public static SpoolerPrinter[] DiscoverQueues()
        {
            var result = new List<SpoolerPrinter>();
            uint flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;

            // أول استدعاء لحساب الحجم المطلوب
            EnumPrintersW(flags, null, 2, IntPtr.Zero, 0, out int needed, out int returned);
            if (needed == 0) return result.ToArray();

            IntPtr buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (!EnumPrintersW(flags, null, 2, buf, needed, out _, out returned))
                    return result.ToArray();

                int structSize = Marshal.SizeOf<PRINTER_INFO_2W>();
                for (int i = 0; i < returned; i++)
                {
                    IntPtr p = buf + i * structSize;
                    var info = Marshal.PtrToStructure<PRINTER_INFO_2W>(p);
                    if (!string.IsNullOrWhiteSpace(info.pPrinterName))
                        result.Add(new SpoolerPrinter(
                            info.pPrinterName,
                            info.pPortName ?? "",
                            info.pDriverName ?? ""));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }

            return result.ToArray();
        }

        /// <summary>
        /// اكتشف طوابير Epson L1800 (اسم يحتوي L1800)
        /// </summary>
        public static SpoolerPrinter[] FindL1800Queues()
        {
            var all = DiscoverQueues();
            var list = new List<SpoolerPrinter>();
            foreach (var q in all)
            {
                if (q.Name.Contains("L1800", StringComparison.OrdinalIgnoreCase) ||
                    q.Name.Contains("L-1800", StringComparison.OrdinalIgnoreCase))
                    list.Add(q);
            }
            return list.ToArray();
        }

        // ── الإرسال الخام ──

        /// <summary>
        /// إرسال بايتات خام (ESC/P2) إلى طابور Windows — datatype RAW
        /// هذه هي الطريقة القياسية لتمرير أوامر أوامر مصفوفة النقاط دون تفسير
        /// </summary>
        public static PrintJobResult SendRaw(string queueName, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(queueName))
                return new PrintJobResult { Success = false, Error = "اسم الطابور فارغ" };
            if (data == null || data.Length == 0)
                return new PrintJobResult { Success = true, BytesSent = 0, Duration = TimeSpan.Zero };

            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (!OpenPrinterW(queueName, out IntPtr hPrinter, IntPtr.Zero))
                return new PrintJobResult
                {
                    Success = false,
                    Error   = $"تعذر فتح الطابور «{queueName}» — Win32 خطأ {Marshal.GetLastWin32Error()}",
                    Duration = sw.Elapsed
                };

            try
            {
                var doc = new DOC_INFO_1W
                {
                    pDocName    = $"ElnagarRIP — {DateTime.Now:HH:mm:ss}",
                    pOutputFile = null!,
                    pDatatype   = "RAW"   // ← خام: يمر للطابعة بلا تفسير
                };

                if (!StartDocPrinterW(hPrinter, 1, ref doc))
                    return new PrintJobResult
                    {
                        Success = false,
                        Error   = $"StartDoc فشل — خطأ {Marshal.GetLastWin32Error()}",
                        Duration = sw.Elapsed
                    };

                try
                {
                    if (!StartPagePrinter(hPrinter))
                        return new PrintJobResult
                        { Success = false, Error = "StartPage فشل", Duration = sw.Elapsed };

                    // الكتابة على دفعات 64KB — سلوك spooler المستقر
                    const int chunk = 64 * 1024;
                    int sent = 0, offset = 0;
                    IntPtr pBuf = Marshal.AllocHGlobal(chunk);
                    try
                    {
                        while (offset < data.Length)
                        {
                            int count = Math.Min(chunk, data.Length - offset);
                            Marshal.Copy(data, offset, pBuf, count);

                            if (!WritePrinter(hPrinter, pBuf, count, out int written))
                                return new PrintJobResult
                                {
                                    Success  = false,
                                    Error    = $"WritePrinter فشل — خطأ {Marshal.GetLastWin32Error()}",
                                    BytesSent = sent,
                                    Duration = sw.Elapsed
                                };

                            sent   += written;
                            offset += written;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pBuf);
                        EndPagePrinter(hPrinter);
                    }

                    sw.Stop();
                    return new PrintJobResult
                    { Success = true, BytesSent = sent, Duration = sw.Elapsed };
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }
}