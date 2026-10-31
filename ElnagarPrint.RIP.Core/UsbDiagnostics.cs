using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ElnagarPrint.RIP.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  UsbDiagnostics — اختبار اتصال كامل بطابعة L1800 (Stage 4)
    //  الخطوات: فحص LibUsb → فحص طوابير Windows → فتح تجريبي → (اختياري) probe
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>نتيجة خطوة تشخيص واحدة</summary>
    public sealed record DiagnosticStep(
        int    Index,
        string Name,
        bool   Passed,
        string Detail);

    /// <summary>تقرير التشخيص الكامل</summary>
    public sealed class UsbDiagnosticReport
    {
        public List<DiagnosticStep> Steps { get; } = new();
        /// <summary>هل يوجد مسار نقل صالح على الأقل؟</summary>
        public bool HasWorkingTransport { get; private set; }
        /// <summary>اسم المسار الذي نجح</summary>
        public string WorkingTransport { get; private set; } = "";
        /// <summary>التوصية العملية للورشة</summary>
        public string Recommendation { get; private set; } = "";

        internal void Finish()
        {
            foreach (var s in Steps)
            {
                if (!s.Passed) continue;
                if (s.Name.Contains("فتح USB"))
                {
                    HasWorkingTransport = true;
                    WorkingTransport = "USB مباشر (WinUSB)";
                    Recommendation = "الطابعة متاحة عبر USB مباشر — الإرسال المباشر البايت-ببايت ممكن.";
                    return;
                }
                if (s.Name.Contains("طابور Windows"))
                {
                    HasWorkingTransport = true;
                    WorkingTransport = "Windows Spooler (RAW)";
                    Recommendation = "درايفر Epson يحجز الجهاز — الإرسال عبر طابور Windows RAW (الوضع العملي الموصى به للورشة).";
                    return;
                }
            }

            Recommendation = "لا يوجد مسار نقل: وصّل الطابعة USB وتشغّلها، " +
                             "وتأكد من تثبيت درايفر Epson L1800 في Windows.";
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🔌 تقرير تشخيص الاتصال — Epson L1800 (VID=04B8, PID=1129)");
            sb.AppendLine(new string('─', 60));
            foreach (var s in Steps)
                sb.AppendLine($"{(s.Passed ? "✅" : "❌")} [{s.Index}] {s.Name} — {s.Detail}");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine(HasWorkingTransport
                ? $"المسار الصالح: {WorkingTransport}"
                : "لا مسار صالح");
            sb.AppendLine($"التوصية: {Recommendation}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// مشغّل التشخيص — يُستدعى من زر «اختبار الاتصال» في الواجهة
    /// </summary>
    public static class UsbDiagnostics
    {
        /// <param name="sendProbe">true = إرسال 64 بايت اختبارية (سطر تغذية فقط) بعد الفتح</param>
        public static UsbDiagnosticReport RunFullTest(bool sendProbe = false)
        {
            var report = new UsbDiagnosticReport();
            int step = 0;

            // ── ① فحص LibUsb registry ──
            DiscoveredPrinter[] usbPrinters;
            try
            {
                usbPrinters = UsbPrinterPort.DiscoverL1800();
                report.Steps.Add(new DiagnosticStep(++step, "فحص LibUsb registry",
                    true,
                    usbPrinters.Length > 0
                        ? $"وُجدت {usbPrinters.Length} طابعة: {usbPrinters[0].Name}"
                        : "لا جهاز L1800 مرئي لـ LibUsb (طبيعي لو درايفر Epson يحجزه)"));
            }
            catch (Exception ex)
            {
                usbPrinters = Array.Empty<DiscoveredPrinter>();
                report.Steps.Add(new DiagnosticStep(++step, "فحص LibUsb registry",
                    false, $"خطأ: {ex.Message}"));
            }

            // ── ② فحص طوابير Windows ──
            SpoolerPrinter[] queues;
            try
            {
                queues = WindowsSpoolerPort.FindL1800Queues();
                var all = WindowsSpoolerPort.DiscoverQueues();
                report.Steps.Add(new DiagnosticStep(++step, "فحص طوابير Windows",
                    queues.Length > 0,
                    queues.Length > 0
                        ? $"طابور L1800: «{queues[0].Name}» على منفذ {queues[0].Port}"
                        : $"لا طابور L1800 مثبت ({all.Length} طابور آخر مكتشف)"));
            }
            catch (Exception ex)
            {
                queues = Array.Empty<SpoolerPrinter>();
                report.Steps.Add(new DiagnosticStep(++step, "فحص طوابير Windows",
                    false, $"خطأ: {ex.Message}"));
            }

            // ── ③ فتح USB مباشر ──
            if (usbPrinters.Length > 0)
            {
                try
                {
                    using var port = new UsbPrinterPort(new UsbConnectionSettings
                    {
                        VendorId  = EpsonUsbIds.VENDOR_ID_L1800,
                        ProductId = EpsonUsbIds.PRODUCT_ID_L1800
                    });
                    port.OpenL1800();

                    string detail = "تم الفتح + claim الـ interface بنجاح";

                    // ── ④ probe اختياري: سطر تغذية فارغ (ESC + LF)
                    if (sendProbe)
                    {
                        // ESC ( v — تغذية 3 سطور فقط، لا حبر
                        var probe = new EncodedPage(8, 3);
                        probe.Append(ESCP2.CMD_RESET);
                        probe.Append(ESCP2.CMD_LINE_FEED);
                        probe.Append(ESCP2.CMD_LINE_FEED);
                        probe.Append(ESCP2.CMD_LINE_FEED);
                        var r = port.SendPrintJob(probe);
                        detail += r.Success
                            ? $" — probe أُرسل ({r.BytesSent} بايت، {r.Duration.TotalMilliseconds:F0}ms)"
                            : $" — probe فشل: {r.Error}";
                    }

                    report.Steps.Add(new DiagnosticStep(++step, "فتح USB مباشر",
                        true, detail));
                    port.Close();
                }
                catch (Exception ex)
                {
                    report.Steps.Add(new DiagnosticStep(++step, "فتح USB مباشر",
                        false, $"فشل: {ex.Message}"));
                }
            }
            else
            {
                report.Steps.Add(new DiagnosticStep(++step, "فتح USB مباشر",
                    false, "تُخطَّى — لا جهاز في LibUsb registry"));
            }

            // ── ⑤ فتح طابور Windows (بدون طباعة — فتح/إغلاق فقط) ──
            if (queues.Length > 0)
            {
                // اختبار الإرسال الفارغ — يتحقق من صلاحية الطابور دون طباعة
                var empty = Array.Empty<byte>();
                var r = WindowsSpoolerPort.SendRaw(queues[0].Name, empty);
                report.Steps.Add(new DiagnosticStep(++step, "صلاحية طابور Windows",
                    r.Success,
                    r.Success
                        ? $"الطابور «{queues[0].Name}» يقبل المهام ✓"
                        : $"فشل: {r.Error}"));
            }
            else
            {
                report.Steps.Add(new DiagnosticStep(++step, "صلاحية طابور Windows",
                    false, "تُخطَّى — لا طابور L1800 مثبت"));
            }

            report.Finish();
            return report;
        }
    }
}