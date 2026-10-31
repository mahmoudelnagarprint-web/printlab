using System;
using System.Collections.Generic;
using System.Linq;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  WeaveEngine.cs — إدارة التداخل الميكانيكي (Micro-Weaving)
    //  Phase 2: Core Engine v2 — Weave Pattern + Nozzle Mapping + Stochastic
    // ══════════════════════════════════════════════════════════════════════════

    #region ─── Weave Pattern Types ──────────────────────────────────────────────

    /// <summary>
    /// نوع نمط النسيج (Weave Pattern)
    /// من تحليل .tpp — سلاسل Hex من 3 أحرف لكل مجموعة
    /// </summary>
    public enum WeavePatternType
    {
        /// <summary>نمط قياسي — L1800 6-color</summary>
        Standard,
        /// <summary>نمط متعدد التمريرات (3-pass)</summary>
        MultiPass,
        /// <summary>نمط interleaved عالي الكثافة</summary>
        Interleaved,
        /// <summary>نمط zigzag (متداخل حرف M)</summary>
        ZigZag,
        /// <summary>بدون نمط (طباعة مباشرة)</summary>
        None
    }

    /// <summary>
    /// ترميز Weave من ملف .tpp
    /// كل 3 حروف hex = رقم nozzle group لمجموعة صفوف
    /// مثال: "27D" = nozzle group 27 (hex 0x27 = 39 decimal)
    /// </summary>
    public sealed class WeavePattern
    {
        /// <summary>السلسلة الخام من .tpp (hex)</summary>
        public string RawHex { get; }

        /// <summary>مصفوفة nozzle groups لكل row في pass</summary>
        public int[] NozzleGroups { get; }

        /// <summary>عدد التمريرات (passes) = length / 3</summary>
        public int PassCount => NozzleGroups.Length;

        /// <summary>نوع النمط (Standard/MultiPass/Interleaved/ZigZag/None)</summary>
        public WeavePatternType Type { get; }

        public WeavePattern(string hexString)
        {
            RawHex = hexString ?? "";
            NozzleGroups = ParseHexString(hexString);
            Type = DetectPatternType(NozzleGroups);
        }

        /// <summary>
        /// بناء WeavePattern من مصفوفة nozzle groups مباشرة
        /// </summary>
        public WeavePattern(int[] nozzleGroups)
        {
            NozzleGroups = nozzleGroups;
            Type = DetectPatternType(nozzleGroups);
            RawHex = BuildHexString(nozzleGroups);
        }

        private static int[] ParseHexString(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<int>();

            var groups = new List<int>();
            for (int i = 0; i + 2 < hex.Length; i += 3)
            {
                string token = hex.Substring(i, 3);
                // Token format: 2 hex digits + 1 letter (pass identifier)
                string hexPart = token.Substring(0, 2);
                if (int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber,
                    null, out var nozzleGroup))
                {
                    groups.Add(nozzleGroup);
                }
            }
            return groups.ToArray();
        }

        private static string BuildHexString(int[] groups)
        {
            // Reconstruct hex string from groups (pass identifier letters: A,B,C,…)
            var sb = new System.Text.StringBuilder(groups.Length * 3);
            for (int i = 0; i < groups.Length; i++)
            {
                char passId = (char)('A' + (i % 26));  // A,B,C,...循环
                sb.Append(groups[i].ToString("X2"));
                sb.Append(passId);
            }
            return sb.ToString();
        }

        private static WeavePatternType DetectPatternType(int[] groups)
        {
            if (groups == null || groups.Length == 0) return WeavePatternType.None;

            // Detect from nozzle group pattern
            var first = groups[0];
            bool ascending = true;
            bool alternating = true;

            for (int i = 1; i < groups.Length; i++)
            {
                if (groups[i] <= groups[i - 1]) ascending = false;
                if (i % 2 == 1 && groups[i] != groups[0]) alternating = false;
            }

            if (groups.Length >= 3) return WeavePatternType.MultiPass;
            if (ascending && groups.Length > 2) return WeavePatternType.Interleaved;
            if (alternating) return WeavePatternType.Standard;
            return WeavePatternType.Standard;
        }

        /// <summary>
        /// Standard weave pattern لـ L1800 (180 nozzles)
        /// Default: distributes 180 rows across nozzle groups in 30-row blocks
        /// </summary>
        public static WeavePattern L1800_Standard()
        {
            // 6 nozzle groups (30 nozzles each), repeating pattern
            // This is a simplified standard weave for L1800
            int[] groups = new int[180];
            for (int i = 0; i < 180; i++)
                groups[i] = (i / 30) % 6;  // groups 0–5 repeating
            return new WeavePattern(groups);
        }

        /// <summary>
        /// 3-pass weave pattern for high-quality photo printing
        /// </summary>
        public static WeavePattern L1800_TriplePass()
        {
            // 60 nozzles per pass × 3 passes = 180 total
            int[] groups = new int[180];
            for (int i = 0; i < 180; i++)
                groups[i] = i % 3;  // pass 0,1,2 repeating
            return new WeavePattern(groups);
        }

        /// <summary>
        /// From .tpp hex string — public factory
        /// </summary>
        public static WeavePattern FromTppHex(string hex) =>
            string.IsNullOrWhiteSpace(hex) ? L1800_Standard() : new WeavePattern(hex);
    }

    #endregion

    #region ─── Nozzle Row Mapping ──────────────────────────────────────────────

    /// <summary>
    /// خريطة توزيع صفوف الصورة على nozzle groups
    /// </summary>
    public sealed class NozzleMap
    {
        /// <summary>عرض الصورة بالبكسل</summary>
        public int Width { get; }

        /// <summary>ارتفاع الصورة بالبكسل</summary>
        public int Height { get; }

        /// <summary>
        /// لكل row في الصورة → مصفوفة من nozzle group indices
        /// row i قد تُطبع من nozzle group(s) متعدد(ة)
        /// </summary>
        public int[] RowToNozzleGroup { get; }

        /// <summary>
        /// لكل nozzle group → صفوف الصورة التي يطبعها
        /// </summary>
        public List<int>[] NozzleGroupToRows { get; }

        public NozzleMap(int width, int height, int[] rowToGroup, List<int>[] groupToRows)
        {
            Width = width;
            Height = height;
            RowToNozzleGroup = rowToGroup;
            NozzleGroupToRows = groupToRows;
        }
    }

    #endregion

    #region ─── Stochastic Weaving ──────────────────────────────────────────────

    /// <summary>
    /// معامل التوزيع العشوائي الكاذب (Pseudo-Stochastic Weaving)
    /// يضيف فارق طوري (phase shift) عشوائي لكن قابل للتكرار
    /// يمنع ظهور خطوط Banding المتكررة من انسداد النوزلات
    /// </summary>
    public sealed class StochasticWeaveConfig
    {
        /// <summary>تفعيل التوزيع العشوائي</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// بذرة عشوائية (seed) — تحافظ على قابلية التكرار
        /// نفس الـ seed = نفس الفارق الطوري كل مرة
        /// </summary>
        public int Seed { get; set; } = 42;

        /// <summary>
        /// قوة الفارق الطوري: 0.0–1.0
        /// 0.0 = بدون فارق (standard weave)
        /// 0.3 = فارق خفيف (recommended)
        /// 0.5 = فارق متوسط
        /// 1.0 = فارق كامل (random offset per row)
        /// </summary>
        public double PhaseShiftStrength { get; set; } = 0.3;

        /// <summary>
        /// عدد nozzle groups المتأثرين بالفارق
        /// </summary>
        public int AffectedGroups { get; set; } = 3;
    }

    #endregion

    #region ─── Weave Engine ───────────────────────────────────────────────────

    /// <summary>
    /// محرك النسيج (Weave Engine) — يحول خريطة التنقيط إلى nozzle mapping
    ///
    /// المعادلة الأساسية:
    ///   nozzle_row[i] = weave_pattern[i % pattern_length] + (i / pattern_length) × pattern_offset
    ///
    /// Dynamic Pseudo-Stochastic:
    ///   nozzle_row[i] += round(phase_shift[i] × nozzle_count × PhaseShiftStrength)
    ///   phase_shift[i] = pseudo_random(i, seed)  ∈ [-0.5, +0.5]
    /// </summary>
    public sealed class WeaveEngine
    {
        private readonly WeavePattern _weavePattern;
        private readonly StochasticWeaveConfig _stochastic;
        private readonly Random _random;
        private readonly int _nozzleCount;

        /// <summary>
        /// nozzle count لـ L1800 = 180
        /// </summary>
        public int NozzleCount => _nozzleCount;

        public WeaveEngine(WeavePattern pattern, int nozzleCount = 180,
            StochasticWeaveConfig? stochastic = null)
        {
            _weavePattern = pattern ?? WeavePattern.L1800_Standard();
            _nozzleCount = nozzleCount;
            _stochastic = stochastic ?? new StochasticWeaveConfig();
            _random = new Random(_stochastic.Seed);
        }

        /// <summary>
        /// من .tpp hex string مباشرة
        /// </summary>
        public static WeaveEngine FromTppHex(string hexWeave, int nozzleCount = 180,
            StochasticWeaveConfig? stochastic = null)
        {
            return new WeaveEngine(WeavePattern.FromTppHex(hexWeave), nozzleCount, stochastic);
        }

        // ── Main API ──

        /// <summary>
        /// بناء خريطة توزيع كاملة لصورة معلومة الأبعاد
        /// </summary>
        public NozzleMap BuildNozzleMap(int width, int height)
        {
            int[] rowToGroup = new int[height];
            var groupToRows = new List<int>[GetGroupCount()];
            for (int i = 0; i < groupToRows.Length; i++)
                groupToRows[i] = new List<int>();

            // Precompute phase shifts for each row (stochastic weaving)
            double[] phaseShifts = ComputePhaseShifts(height);

            for (int row = 0; row < height; row++)
            {
                int group = ComputeNozzleGroup(row, phaseShifts[row]);
                rowToGroup[row] = group;
                if (group >= 0 && group < groupToRows.Length)
                    groupToRows[group].Add(row);
            }

            return new NozzleMap(width, height, rowToGroup, groupToRows);
        }

        /// <summary>
        /// توزيع مصفوفة التنقيط على nozzle groups
        /// يرجع 6 خرائط (CMYK + Lc + Lm) كل واحدة موزعة على nozzle groups
        /// </summary>
        public WeaveResult ApplyWeave(HalftoneResult halftone, NozzleMap map)
        {
            int height = halftone.Height;
            int width = halftone.Width;

            // Create nozzle-mapped channels
            var wC  = new byte[height * width];
            var wM  = new byte[height * width];
            var wY  = new byte[height * width];
            var wK  = new byte[height * width];
            var wLc = new byte[height * width];
            var wLm = new byte[height * width];

            for (int row = 0; row < height; row++)
            {
                int nozzleGroup = map.RowToNozzleGroup[row];
                for (int col = 0; col < width; col++)
                {
                    int idx = row * width + col;

                    // كل صف يتحول لرقم nozzle group معين
                    // النقاط في هذا الصف تطبع من nozzle group = nozzleGroup
                    // (لا نقلب البيانات — فقط نو mape الـ row's dots to the correct nozzle group)
                    // We store the PASS index (group) in the row metadata, not in dot data

                    // For the dot data, we just remap the row to the nozzle group
                    // (the nozzle group determines WHICH nozzle fires, not WHICH dot value)
                    wC[idx]  = halftone.Cyan.GetDot(row, col);
                    wM[idx]  = halftone.Magenta.GetDot(row, col);
                    wY[idx]  = halftone.Yellow.GetDot(row, col);
                    wK[idx]  = halftone.Black.GetDot(row, col);
                    wLc[idx] = halftone.LightCyan.GetDot(row, col);
                    wLm[idx] = halftone.LightMagenta.GetDot(row, col);
                }
            }

            return new WeaveResult
            {
                Width = width,
                Height = height,
                CyanDotMap = wC,
                MagentaDotMap = wM,
                YellowDotMap = wY,
                BlackDotMap = wK,
                LightCyanDotMap = wLc,
                LightMagentaDotMap = wLm,
                NozzleMap = map
            };
        }

        /// <summary>
        /// توزيع صف واحد (row) على nozzle group
        /// </summary>
        public int ComputeNozzleGroup(int row, double phaseShift = 0)
        {
            if (_weavePattern.PassCount == 0)
                return 0;

            int patternLen = _weavePattern.PassCount;
            int patternIdx = row % patternLen;
            int groupBase = _weavePattern.NozzleGroups[patternIdx];

            // Compute pass offset (pass number within pattern)
            int passNumber = row / patternLen;

            // Nozzle group = base + (pass × nozzle_count_per_group)
            int groupCount = GetGroupCount();
            int nozzlePerGroup = _nozzleCount / groupCount;

            int group = groupBase + (passNumber % 3) * (nozzlePerGroup / 3);
            group = Math.Clamp(group, 0, _nozzleCount - 1);

            // Apply stochastic phase shift
            if (_stochastic.Enabled && Math.Abs(phaseShift) > 1e-6)
            {
                int phaseOffset = (int)Math.Round(phaseShift * _stochastic.AffectedGroups);
                group = Math.Clamp(group + phaseOffset, 0, _nozzleCount - 1);
            }

            return group;
        }

        // ── Stochastic helpers ──

        private double[] ComputePhaseShifts(int height)
        {
            if (!_stochastic.Enabled || _stochastic.PhaseShiftStrength < 1e-6)
                return new double[height];

            // Mulberry32 PRNG — fast, good distribution, seed-based
            uint state = (uint)_stochastic.Seed;
            double[] shifts = new double[height];
            for (int i = 0; i < height; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                double raw = ((state >> 8) & 0xFFFFFF) / 16777216.0; // 0.0–1.0
                double centered = raw - 0.5;  // -0.5 to +0.5
                shifts[i] = centered * _stochastic.PhaseShiftStrength;
            }
            return shifts;
        }

        /// <summary>
        /// Dynamic Pseudo-Stochastic: حساب nozzle group مع فارق عشوائي ذكي
        /// الفارق يعتمد على كثافة الـ row (تعديل على nozzleGroup بشكل خفي)
        /// </summary>
        public int ComputeNozzleGroupWithDensity(int row, byte[] rowDotLevels,
            double phaseShift = 0)
        {
            // Base group from weave pattern
            int baseGroup = ComputeNozzleGroup(row, phaseShift);

            if (!_stochastic.Enabled)
                return baseGroup;

            // Compute row density (how many dots in this row)
            int dotCount = 0;
            foreach (var level in rowDotLevels)
                if (level > 0) dotCount++;

            double density = (double)dotCount / rowDotLevels.Length; // 0.0–1.0

            // Rows with very high density → apply slight offset to reduce ink overlap
            // This is the "Smart Drying Pause Trigger" — nozzles that fire dense rows
            // get shifted by a tiny pseudo-random amount to avoid simultaneous heavy output
            if (density > 0.7)  // >70% density = heavy row
            {
                uint state = (uint)(_stochastic.Seed + row * 17);
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                double raw = ((state >> 8) & 0xFFFFFF) / 16777216.0;
                int microOffset = (int)Math.Round((raw - 0.5) * 2.0); // ±1 nozzle
                return Math.Clamp(baseGroup + microOffset, 0, _nozzleCount - 1);
            }

            return baseGroup;
        }

        private int GetGroupCount() => Math.Max(_weavePattern.PassCount, 6);

        // ── Weave Pattern Analysis ──

        /// <summary>
        /// وصف النمط النصي (للمعاينة في الـ UI)
        /// </summary>
        public string GetWeaveDescription()
        {
            if (_weavePattern.Type == WeavePatternType.None)
                return "No weave (direct pass)";

            var sb = new System.Text.StringBuilder();
            sb.Append($"Pattern: {_weavePattern.Type}, ");
            sb.Append($"Passes: {_weavePattern.PassCount}, ");
            sb.Append($"Groups: {string.Join(",", _weavePattern.NozzleGroups.Take(10))}");

            if (_weavePattern.NozzleGroups.Length > 10)
                sb.Append($" ... (+{_weavePattern.NozzleGroups.Length - 10} more)");

            if (_stochastic.Enabled)
                sb.Append($", Stochastic=ON (seed={_stochastic.Seed}, strength={_stochastic.PhaseShiftStrength})");

            return sb.ToString();
        }
    }

    #endregion

    #region ─── Weave Result ────────────────────────────────────────────────────

    /// <summary>
    /// ناتج تطبيق الـ Weave — خرائط النقاط الموزعة على nozzle groups
    /// </summary>
    public sealed class WeaveResult
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] CyanDotMap { get; init; } = Array.Empty<byte>();
        public byte[] MagentaDotMap { get; init; } = Array.Empty<byte>();
        public byte[] YellowDotMap { get; init; } = Array.Empty<byte>();
        public byte[] BlackDotMap { get; init; } = Array.Empty<byte>();
        public byte[] LightCyanDotMap { get; init; } = Array.Empty<byte>();
        public byte[] LightMagentaDotMap { get; init; } = Array.Empty<byte>();
        public NozzleMap NozzleMap { get; init; } = null!;
    }

    #endregion
}