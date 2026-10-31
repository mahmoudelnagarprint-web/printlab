using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  TppTpmParser.cs — قارئ شامل لملفات تعريف الطابعات (.tpp) وجداول الألوان (.tpm)
    //  PrintFab 1.x Compatible — Clean-room reimplementation
    // ══════════════════════════════════════════════════════════════════════════

    #region ─────────────────────────────────────────────
    //  .tpp — Printer Definition File
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// وصف كامل لملف تعريف طابعة واحد (.tpp)
    /// </summary>
    public sealed class TppPrinterDefinition
    {
        public string FileName { get; set; } = "";

        // [Driver]
        public string DriverId { get; set; } = "";
        public int DriverNumber { get; set; }
        public int DriverParam { get; set; }

        // [Cartridge0..N]
        public List<string> CartridgeMatchtables { get; set; } = new();

        // [Paper0..N]
        public List<TppPaper> Papers { get; set; } = new();

        // [ModeX_Y_Z]
        public List<TppMode> Modes { get; set; } = new();

        // [Size0..N]
        public List<TppSize> Sizes { get; set; } = new();

        // [Feed0..N]
        public List<TppFeed> Feeds { get; set; } = new();

        // [Control0..N]
        public List<TppControl> Controls { get; set; } = new();

        // [Defaults]
        public int DefaultQuality { get; set; }
        public int DefaultPaper { get; set; }
        public int DefaultSize { get; set; }
        public int DefaultFeed { get; set; }

        /// <summary>
        /// استرجاع الـ Mode بالمؤشر [mode, quality_preset, paper_class]
        /// </summary>
        public TppMode? FindMode(int mode, int qualityPreset, int paperClass)
        {
            return Modes.FirstOrDefault(m =>
                m.ModeNumber == mode &&
                m.QualityPreset == qualityPreset &&
                m.PaperClass == paperClass);
        }

        /// <summary>
        /// استرجاع Paper بالنوع (index)
        /// </summary>
        public TppPaper? GetPaper(int index) =>
            index >= 0 && index < Papers.Count ? Papers[index] : null;

        /// <summary>
        /// استرجاع Size بالمؤشر
        /// </summary>
        public TppSize? GetSize(int index) =>
            index >= 0 && index < Sizes.Count ? Sizes[index] : null;

        /// <summary>
        /// استرجاع Feed بالمؤشر
        /// </summary>
        public TppFeed? GetFeed(int index) =>
            index >= 0 && index < Feeds.Count ? Feeds[index] : null;
    }

    public sealed class TppPaper
    {
        public int Index { get; set; }
        public int Gamma { get; set; } = 300;
        public int Saturation { get; set; } = 100;
        public string Match { get; set; } = "";         // .tpm filename
        /// <summary>ink_limit, drying_time, head_height, interleaving</summary>
        public int InkLimit { get; set; } = 300;
        public int DryingTime { get; set; }
        public int HeadHeight { get; set; }
        public int Interleaving { get; set; }
    }

    public sealed class TppMode
    {
        public string SectionName { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quality { get; set; }                          // 0–7
        public (int H, int V) ApplicationDpi { get; set; }
        public (int H, int V) PrinterDpi { get; set; }
        public string Curves { get; set; } = "";                 // section name in .tpm
        public string Weave { get; set; } = "";                  // hex string
        /// <summary>mode, quality_preset, paper_class, ink_limit_value</summary>
        public int ModeNumber { get; set; }
        public int QualityPreset { get; set; }
        public int PaperClass { get; set; }
        public int InkLimitValue { get; set; }

        /// <summary>تعدد التمريرات من الـ Param (mode >= 18 → multi-pass)</summary>
        public int EstimatedPasses => ModeNumber switch
        {
            >= 18 => 3,   // Photo / Ultra Photo — 3 passes
            >= 12 => 2,   // High quality — 2 passes
            _     => 1    // Draft / Normal — 1 pass
        };
    }

    public sealed class TppSize
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public (int W, int H) MinSize { get; set; }
        public (int W, int H) Size { get; set; }
        public (int L, int T, int R, int B) Margins { get; set; }
        public int ANumber { get; set; }      // A4=3, A3=5, A2=4
        public int WNumber { get; set; }
        public bool Borderless { get; set; }
    }

    public sealed class TppFeed
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public int Param { get; set; }
    }

    public sealed class TppControl
    {
        public int Index { get; set; }
        public string Type { get; set; } = "";   // INK, NOZZLECHECK, CLEAN, ALIGN
        public int Param { get; set; }
    }

    #endregion

    #region ─────────────────────────────────────────────
    //  .tpm — Color Match Table File
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// وصف كامل لملف جدول الألوان (.tpm)
    /// </summary>
    public sealed class TpmColorTable
    {
        public string FileName { get; set; } = "";

        /// <summary>
        /// أقسام الدقة — كل قسم يحتوي LUT خاص به
        /// </summary>
        public List<TpmResolutionCurve> Curves { get; set; } = new();

        /// <summary>
        /// استرجاع المنحنيات بالدقة المطلوبة
        /// </summary>
        public TpmResolutionCurve? GetCurve(string curveName) =>
            Curves.FirstOrDefault(c =>
                c.SectionName.Equals(curveName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// استرجاع أول منحنى متاح (fallback)
        /// </summary>
        public TpmResolutionCurve? GetDefaultCurve() => Curves.FirstOrDefault();

        /// <summary>
        /// القنوات المدعومة في هذا الملف
        /// </summary>
        public string[] GetSupportedChannels()
        {
            var def = GetDefaultCurve();
            return def?.Channels ?? Array.Empty<string>();
        }

        /// <summary>
        /// هل يدعم 6 ألوان (Lc + Lm)
        /// </summary>
        public bool SupportsLightInks()
        {
            var ch = GetSupportedChannels();
            return ch.Contains("lc", StringComparer.OrdinalIgnoreCase) ||
                   ch.Contains("lm", StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// منحنى لوني لدقة واحدة (مثل 1440dpi أو 2880dpi)
    /// </summary>
    public sealed class TpmResolutionCurve
    {
        public string SectionName { get; set; } = "";   // e.g. "1440dpi_d120"

        /// <summary>
        /// القنوات المفعلة: k,c,m,y[,lc,lm[,wh]]
        /// </summary>
        public string[] Channels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// قيمة Gamma (200–550, typically 300)
        /// </summary>
        public int Gamma { get; set; } = 300;

        /// <summary>
        /// عمق الـ dithering: 1 أو 2 (1-bit = 2 levels, 2-bit = 4 levels)
        /// </summary>
        public int Bits { get; set; } = 2;

        /// <summary>
        /// قيم مستويات الـ quantization (e.g. [0, 30, 60, 100])
        /// </summary>
        public double[] Levels { get; set; } = new double[] { 0, 30, 60, 100 };

        /// <summary>
        /// درجة الإشباع (75–100)
        /// </summary>
        public int Saturation { get; set; } = 90;

        /// <summary>
        /// جداول LValues — 32 مدخل لكل قناة
        /// LValues[channel][i] — i=0..31
        /// </summary>
        public Dictionary<string, int[]> LValues { get; set; } = new();

        /// <summary>
        /// جداول Density — 32 مدخل لكل قناة
        /// Density[channel][i] — i=0..31
        /// </summary>
        public Dictionary<string, int[]> Density { get; set; } = new();

        // ── Helper methods ──

        /// <summary>
        /// تحويل قيمة إدخال (0–255) إلى قيمة المعالجة عبر LValues
        /// </summary>
        public int ApplyLValue(string channel, int input256)
        {
            if (!LValues.TryGetValue(channel.ToLower(), out var lut))
                return input256;
            int index = Math.Clamp(input256 >> 3, 0, 31);  // 0–255 → 0–31
            return lut[index];
        }

        /// <summary>
        /// تحويل قيمة إدخال (0–255) إلى كثافة النقطة عبر Density
        /// </summary>
        public int ApplyDensity(string channel, int input256)
        {
            if (!Density.TryGetValue(channel.ToLower(), out var lut))
                return input256;
            int index = Math.Clamp(input256 >> 3, 0, 31);
            return lut[index];
        }

        /// <summary>
        /// تطبيق Gamma curve على قيمة (0–255)
        /// </summary>
        public double ApplyGamma(double normalizedValue)
        {
            // gamma_normalized = Gamma / 100.0
            // output = input ^ (100/gamma)
            double g = 100.0 / Gamma;
            return Math.Pow(normalizedValue, g);
        }

        /// <summary>
        /// تحويل قيمة (0–255) إلى أقرب مستوى في Levels
        /// </summary>
        public double QuantizeToLevel(double value0to255)
        {
            double normalized = value0to255 / 255.0;  // 0.0–1.0 → percentage
            double best = Levels[0];
            double minDiff = Math.Abs(normalized * 100 - best);
            foreach (double level in Levels)
            {
                double diff = Math.Abs(normalized * 100 - level);
                if (diff < minDiff) { minDiff = diff; best = level; }
            }
            return best / 100.0;  // back to 0.0–1.0
        }

        /// <summary>
        /// عدد مستويات الـ quantization = 2^Bits
        /// </summary>
        public int LevelCount => 1 << Bits;
    }

    #endregion

    #region ─────────────────────────────────────────────
    //  Parser
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// محلل ملفات .tpp و .tpm — يعتمد على ParseIni فقط
    /// </summary>
    public static class TppTpmParser
    {
        // ── Public API ──

        /// <summary>
        /// تحليل ملف .tpp كامل — يرجع TppPrinterDefinition
        /// </summary>
        public static TppPrinterDefinition ParseTpp(string filePath)
        {
            var sections = ParseIni(File.ReadAllText(filePath));
            var name = Path.GetFileName(filePath);
            var result = new TppPrinterDefinition { FileName = name };

            foreach (var (sectionKey, sectionData) in sections)
            {
                string s = sectionKey.ToUpperInvariant();

                if (s == "DRIVER")
                    ParseDriver(sectionData, result);
                else if (s.StartsWith("CARTRIDGE"))
                    ParseCartridge(sectionKey, sectionData, result);
                else if (s.StartsWith("PAPER") && s != "DEFAULTS")
                    ParsePaper(sectionKey, sectionData, result);
                else if (s.StartsWith("MODE"))
                    ParseMode(sectionKey, sectionData, result);
                else if (s.StartsWith("SIZE"))
                    ParseSize(sectionKey, sectionData, result);
                else if (s.StartsWith("FEED"))
                    ParseFeed(sectionKey, sectionData, result);
                else if (s.StartsWith("CONTROL"))
                    ParseControl(sectionKey, sectionData, result);
                else if (s == "DEFAULTS")
                    ParseDefaults(sectionData, result);
            }

            return result;
        }

        /// <summary>
        /// تحليل ملف .tpm كامل — يرجع TpmColorTable
        /// </summary>
        public static TpmColorTable ParseTpm(string filePath)
        {
            var sections = ParseIni(File.ReadAllText(filePath));
            var name = Path.GetFileName(filePath);
            var result = new TpmColorTable { FileName = name };

            foreach (var (sectionKey, sectionData) in sections)
            {
                // كل section في .tpm هو منحنى دقة
                var curve = ParseResolutionCurve(sectionKey, sectionData);
                if (curve != null)
                    result.Curves.Add(curve);
            }

            return result;
        }

        /// <summary>
        /// تحميل ملف .tpp من مجلد
        /// </summary>
        public static TppPrinterDefinition LoadTpp(string printersFolder, string fileName)
        {
            string path = Path.Combine(printersFolder, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Printer definition not found: {path}");
            return ParseTpp(path);
        }

        /// <summary>
        /// تحميل ملف .tpm من مجلد
        /// </summary>
        public static TpmColorTable LoadTpm(string colorsFolder, string fileName)
        {
            string path = Path.Combine(colorsFolder, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Color table not found: {path}");
            return ParseTpm(path);
        }

        /// <summary>
        /// استرجاع جميع ملفات .tpp في مجلد
        /// </summary>
        public static string[] ListTppFiles(string printersFolder) =>
            Directory.GetFiles(printersFolder, "*.tpp");

        /// <summary>
        /// استرجاع جميع ملفات .tpm في مجلد
        /// </summary>
        public static string[] ListTpmFiles(string colorsFolder) =>
            Directory.GetFiles(colorsFolder, "*.tpm");

        // ── INI Core ──

        /// <summary>
        /// محلل INI بسيط — يرجع Dictionary[section, Dictionary[key, value]]
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> ParseIni(string content)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? current = null;
            string currentSection = "";

            foreach (var rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[currentSection] = current!;
                }
                else if (current != null && line.Contains('='))
                {
                    int eq = line.IndexOf('=');
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    current[key] = val;
                }
            }

            return result;
        }

        // ── .tpp Parsers ──

        private static void ParseDriver(Dictionary<string, string> data, TppPrinterDefinition result)
        {
            result.DriverId = data.GetValueOrDefault("Id", "");
            result.DriverNumber = int.TryParse(data.GetValueOrDefault("Number", "0"), out var n) ? n : 0;
            result.DriverParam = int.TryParse(data.GetValueOrDefault("Param", "0"), out var p) ? p : 0;
        }

        private static void ParseCartridge(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            var matchtable = data.GetValueOrDefault("Matchtable", "");
            if (!string.IsNullOrWhiteSpace(matchtable))
                result.CartridgeMatchtables.Add(matchtable);
        }

        private static void ParsePaper(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            // [Paper0], [Paper1], …
            var m = Regex.Match(section, @"Paper(\d+)", RegexOptions.IgnoreCase);
            int index = m.Success && int.TryParse(m.Groups[1].Value, out var i) ? i : result.Papers.Count;

            var paper = new TppPaper
            {
                Index = index,
                Gamma = int.TryParse(data.GetValueOrDefault("Gamma", "300"), out var g) ? g : 300,
                Saturation = int.TryParse(data.GetValueOrDefault("Saturation", "100"), out var sat) ? sat : 100,
                Match = data.GetValueOrDefault("Match", "")
            };

            // Parse Param = ink_limit, drying_time, head_height, interleaving
            var param = data.GetValueOrDefault("Param", "");
            var parts = param.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 1) paper.InkLimit = int.TryParse(parts[0], out var il) ? il : 300;
            if (parts.Length >= 2) paper.DryingTime = int.TryParse(parts[1], out var dt) ? dt : 0;
            if (parts.Length >= 3) paper.HeadHeight = int.TryParse(parts[2], out var hh) ? hh : 0;
            if (parts.Length >= 4) paper.Interleaving = int.TryParse(parts[3], out var iv) ? iv : 0;

            result.Papers.Add(paper);
        }

        private static void ParseMode(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            // Section = [Mode0_1_3] → mode=0, quality_preset=1, paper_class=3
            var m = Regex.Match(section, @"Mode(\d+)_(\d+)_(\d+)", RegexOptions.IgnoreCase);
            int modeNum = m.Success && int.TryParse(m.Groups[1].Value, out var mn) ? mn : 0;
            int qPres   = m.Success && int.TryParse(m.Groups[2].Value, out var qp) ? qp : 0;
            int pClass  = m.Success && int.TryParse(m.Groups[3].Value, out var pc) ? pc : 0;

            var mode = new TppMode
            {
                SectionName = section,
                Name = data.GetValueOrDefault("Name", ""),
                Quality = int.TryParse(data.GetValueOrDefault("Quality", "0"), out var q) ? q : 0,
                Curves = data.GetValueOrDefault("Curves", ""),
                Weave = data.GetValueOrDefault("Weave", ""),
                ModeNumber = modeNum,
                QualityPreset = qPres,
                PaperClass = pClass
            };

            // ApplicationDPI = h,v
            var appDpi = data.GetValueOrDefault("ApplicationDPI", "0,0").Split(',');
            mode.ApplicationDpi = (
                int.TryParse(appDpi.ElementAtOrDefault(0), out var ah) ? ah : 0,
                int.TryParse(appDpi.ElementAtOrDefault(1), out var av) ? av : 0
            );

            // PrinterDPI = h,v
            var prtDpi = data.GetValueOrDefault("PrinterDPI", "0,0").Split(',');
            mode.PrinterDpi = (
                int.TryParse(prtDpi.ElementAtOrDefault(0), out var ph) ? ph : 0,
                int.TryParse(prtDpi.ElementAtOrDefault(1), out var pv) ? pv : 0
            );

            // Param = mode, quality_preset, paper_class, ink_limit_value
            var param = data.GetValueOrDefault("Param", "").Split(',');
            if (param.Length >= 4)
            {
                mode.ModeNumber    = int.TryParse(param[0], out var p0) ? p0 : mode.ModeNumber;
                mode.QualityPreset = int.TryParse(param[1], out var p1) ? p1 : mode.QualityPreset;
                mode.PaperClass    = int.TryParse(param[2], out var p2) ? p2 : mode.PaperClass;
                mode.InkLimitValue = int.TryParse(param[3], out var p3) ? p3 : 300;
            }
            else if (param.Length > 0)
            {
                mode.InkLimitValue = int.TryParse(param[^1], out var ilv) ? ilv : 300;
            }

            result.Modes.Add(mode);
        }

        private static void ParseSize(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            var m = Regex.Match(section, @"Size(\d+)", RegexOptions.IgnoreCase);
            int index = m.Success && int.TryParse(m.Groups[1].Value, out var i) ? i : result.Sizes.Count;

            var size = new TppSize
            {
                Index = index,
                Name = data.GetValueOrDefault("Name", ""),
                ANumber = int.TryParse(data.GetValueOrDefault("ANumber", "0"), out var an) ? an : 0,
                WNumber = int.TryParse(data.GetValueOrDefault("WNumber", "0"), out var wn) ? wn : 0,
                Borderless = int.TryParse(data.GetValueOrDefault("BorderlessMask", "0"), out var bm) && bm > 0
            };

            // Minsize = w,h
            var minS = data.GetValueOrDefault("Minsize", "0,0").Split(',');
            size.MinSize = (
                int.TryParse(minS.ElementAtOrDefault(0), out var mw) ? mw : 0,
                int.TryParse(minS.ElementAtOrDefault(1), out var mh) ? mh : 0
            );

            // Size = w,h
            var sz = data.GetValueOrDefault("Size", "0,0").Split(',');
            size.Size = (
                int.TryParse(sz.ElementAtOrDefault(0), out var sw) ? sw : 0,
                int.TryParse(sz.ElementAtOrDefault(1), out var sh) ? sh : 0
            );

            // Margins = l,t,r,b
            var mg = data.GetValueOrDefault("Margins", "0,0,0,0").Split(',');
            size.Margins = (
                int.TryParse(mg.ElementAtOrDefault(0), out var ml) ? ml : 0,
                int.TryParse(mg.ElementAtOrDefault(1), out var mt) ? mt : 0,
                int.TryParse(mg.ElementAtOrDefault(2), out var mr) ? mr : 0,
                int.TryParse(mg.ElementAtOrDefault(3), out var mb) ? mb : 0
            );

            result.Sizes.Add(size);
        }

        private static void ParseFeed(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            var m = Regex.Match(section, @"Feed(\d+)", RegexOptions.IgnoreCase);
            int index = m.Success && int.TryParse(m.Groups[1].Value, out var i) ? i : result.Feeds.Count;

            result.Feeds.Add(new TppFeed
            {
                Index = index,
                Name = data.GetValueOrDefault("Name", ""),
                Param = int.TryParse(data.GetValueOrDefault("Param", "0"), out var p) ? p : 0
            });
        }

        private static void ParseControl(string section, Dictionary<string, string> data, TppPrinterDefinition result)
        {
            var m = Regex.Match(section, @"Control(\d+)", RegexOptions.IgnoreCase);
            int index = m.Success && int.TryParse(m.Groups[1].Value, out var i) ? i : result.Controls.Count;

            result.Controls.Add(new TppControl
            {
                Index = index,
                Type = data.GetValueOrDefault("Type", ""),
                Param = int.TryParse(data.GetValueOrDefault("Param", "0"), out var p) ? p : 0
            });
        }

        private static void ParseDefaults(Dictionary<string, string> data, TppPrinterDefinition result)
        {
            result.DefaultQuality = int.TryParse(data.GetValueOrDefault("Quality", "0"), out var q) ? q : 0;
            result.DefaultPaper    = int.TryParse(data.GetValueOrDefault("Paper", "0"), out var p) ? p : 0;
            result.DefaultSize     = int.TryParse(data.GetValueOrDefault("Size", "0"), out var s) ? s : 0;
            result.DefaultFeed     = int.TryParse(data.GetValueOrDefault("Feed", "0"), out var f) ? f : 0;
        }

        // ── .tpm Parsers ──

        private static TpmResolutionCurve? ParseResolutionCurve(string section, Dictionary<string, string> data)
        {
            if (string.IsNullOrWhiteSpace(section) || data.Count == 0)
                return null;

            var curve = new TpmResolutionCurve
            {
                SectionName = section
            };

            // Channels = k,c,m,y[,lc,lm]
            var ch = data.GetValueOrDefault("Channels", "");
            curve.Channels = ch.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim().ToLower()).ToArray();

            curve.Gamma = int.TryParse(data.GetValueOrDefault("Gamma", "300"), out var g) ? g : 300;
            curve.Bits = int.TryParse(data.GetValueOrDefault("Bits", "2"), out var b) ? b : 2;

            // Levels = v0,v1,v2,...
            var lvls = data.GetValueOrDefault("Levels", "0,30,60,100")
                            .Split(',', StringSplitOptions.TrimEntries);
            curve.Levels = lvls.Select(l => double.TryParse(l, out var v) ? v / 100.0 : 0.0).ToArray();

            curve.Saturation = int.TryParse(data.GetValueOrDefault("Saturation", "90"), out var sat) ? sat : 90;

            // Parse 32-entry LValues and Density for each channel
            foreach (string channel in curve.Channels)
            {
                var lvKey = $"LValues_{channel}";
                var dnKey = $"Density_{channel}";

                if (data.TryGetValue(lvKey, out var lvRaw))
                    curve.LValues[channel] = ParseIntArray32(lvRaw);

                if (data.TryGetValue(dnKey, out var dnRaw))
                    curve.Density[channel] = ParseIntArray32(dnRaw);
            }

            return curve;
        }

        /// <summary>
        /// Parse comma-separated 32 integers for LValues/Density LUTs
        /// </summary>
        private static int[] ParseIntArray32(string raw)
        {
            var parts = raw.Split(',', StringSplitOptions.TrimEntries);
            var result = new int[32];
            for (int i = 0; i < 32; i++)
            {
                if (i < parts.Length)
                    result[i] = int.TryParse(parts[i], out var v) ? v : 0;
            }
            return result;
        }
    }
    #endregion
}
