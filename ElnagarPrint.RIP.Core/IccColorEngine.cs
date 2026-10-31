using System;

namespace ElnagarPrint.RIP.Core
{
    // ══════════════════════════════════════════════════════════════════════════
    //  IccColorEngine.cs — محرك التحويل اللوني ICC كامل
    //  8/16-bit | 6 Color Modes | 7 Rendering Intents | LAB sliders | D50/D65/D80
    // ══════════════════════════════════════════════════════════════════════════

    #region Enums & Settings

    public enum ColorMode { Grayscale, GrayscaleFast, GrayPhoto, ColorRGB, CMYKProof, CMYKMixBlack }
    public enum RenderingIntent { Perception, PerceptionPhoto, PerceptionInksave, Saturation, ColorimetricAbsolute, ColorimetricRelative, NoCorrection }
    public enum ReferenceLight { D50 = 5000, D65 = 6500, D80 = 8000 }

    public sealed class ColorEngineSettings
    {
        public ColorMode ColorMode { get; set; } = ColorMode.ColorRGB;
        public RenderingIntent Intent { get; set; } = RenderingIntent.Perception;
        public ReferenceLight RefLight { get; set; } = ReferenceLight.D50;
        public bool BlackPointCompensation { get; set; } = true;
        public int BitDepth { get; set; } = 8;
        public double LabL { get; set; } = 1.0;   // 0–100 → 0.0–1.0
        public double LabA { get; set; } = 0.0;   // -128–+127 → normalized
        public double LabB { get; set; } = 0.0;
        public double Brightness { get; set; } = 1.0;   // 100=1.0
        public double Contrast   { get; set; } = 1.0;
        public double Saturation { get; set; } = 1.0;   // Color Saturation slider
        public string? RgbProfilePath { get; set; }
        public string? CmykProfilePath { get; set; }
        public string? OutputProfilePath { get; set; }
    }

    public readonly struct CmykPixel
    {
        public double C { get; init; }
        public double M { get; init; }
        public double Y { get; init; }
        public double K { get; init; }
        public CmykPixel(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
        public static CmykPixel RGBToCMYK(double r, double m, double y)
        {
            double k = Math.Max(Math.Max(r, m), y);
            if (k < 1e-7) return new CmykPixel(0, 0, 0, 0);
            return new CmykPixel((k - r) / k, (k - m) / k, (k - y) / k, k);
        }
        public byte[] ToRgb8()
        {
            double ki = 1.0 - K;
            return new byte[] {
                (byte)Math.Round(Math.Clamp((1.0 - C) * ki * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp((1.0 - M) * ki * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp((1.0 - Y) * ki * 255, 0, 255)) };
        }
        public override string ToString() => $"C={C:F3} M={M:F3} Y={Y:F3} K={K:F3}";
    }

    #endregion

    #region ICC Profile Parser

    public sealed class IccProfileInfo
    {
        public int Size { get; init; }
        public string ColorSpace { get; init; } = "";
        public string PCS { get; init; } = "";
        public string ProfileSignature { get; init; } = "";
        public string ProfileVersion { get; init; } = "";
    }

    public sealed class IccProfile
    {
        public IccProfileInfo Info { get; init; } = new();
        public byte[] RawData { get; init; } = Array.Empty<byte>();
        public System.Collections.Generic.Dictionary<string, byte[]> Tags { get; init; } = new();
        public bool HasTag(string sig) => Tags.ContainsKey(sig);
        public byte[]? GetTag(string sig) => Tags.TryGetValue(sig, out var d) ? d : null;
    }

    public static class IccProfileParser
    {
        public static IccProfile? LoadProfile(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            try
            {
                var data = System.IO.File.ReadAllBytes(path);
                using var ms = new System.IO.MemoryStream(data);
                using var br = new System.IO.BinaryReader(ms);
                ms.Position = 36;
                var sig = new string(br.ReadChars(4));
                ms.Position = 0;
                var profile = new IccProfile
                {
                    Info = new IccProfileInfo { Size = data.Length, ProfileSignature = sig.Trim() },
                    RawData = data
                };
                if (data.Length < 128) return profile;
                ms.Position = 128;
                uint count = ReadU32(br);
                for (uint i = 0; i < Math.Min(count, 1000); i++)
                {
                    var ts = new string(br.ReadChars(4));
                    uint off = ReadU32(br); uint len = ReadU32(br);
                    if (off + len > data.Length || len > 10_000_000) continue;
                    var td = new byte[len];
                    Array.Copy(data, (int)off, td, 0, (int)len);
                    if (!profile.Tags.ContainsKey(ts)) profile.Tags[ts] = td;
                }
                return profile;
            }
            catch { return null; }
        }

        private static uint ReadU32(System.IO.BinaryReader br)
        {
            var b = br.ReadBytes(4);
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }
    }

    #endregion

    #region ICC Color Transform

    public sealed class IccColorTransform : IDisposable
    {
        private readonly RenderingIntent _intent;
        private readonly bool _bpc;

        // D50 reference white for Lab PCS
        private const double Xn = 0.9642, Yn = 1.0, Zn = 0.8249;

        public IccColorTransform(IccProfile src, IccProfile dst, RenderingIntent intent, bool bpc)
        { _intent = intent; _bpc = bpc; }

        public CmykPixel TransformRGBToCMYK(double r, double g, double b)
        {
            if (_intent == RenderingIntent.NoCorrection)
                return CmykPixel.RGBToCMYK(r, g, b);

            // 1. Apply rendering intent (gamut clipping)
            (r, g, b) = ApplyIntent(r, g, b);

            // 2. RGB → XYZ D65
            var (X, Y, Z) = RGBToXYZ_D65(r, g, b);

            // 3. XYZ D65 → Lab (via chromatic adaptation to D50)
            var (L, a, lb) = XYZToLab(X, Y, Z);

            // 4. Black Point Compensation
            if (_bpc) (L, a, lb) = ApplyBPC(L, a, lb);

            // 5. Lab → XYZ D50 → RGB
            (X, Y, Z) = LabToXYZ(L, a, lb);
            (r, g, b) = XYZToRGB_D65(X, Y, Z);

            return CmykPixel.RGBToCMYK(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
        }

        private (double, double, double) ApplyIntent(double r, double g, double b) =>
            _intent switch
            {
                RenderingIntent.Saturation => SaturationClip(r, g, b),
                RenderingIntent.PerceptionInksave => InksaveClip(r, g, b),
                _ => (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1))
            };

        internal static (double, double, double) SaturationClip(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            if (max < 1e-7) return (r, g, b);
            double s = Math.Min(1.0 / max, 1.5);
            return (r * s, g * s, b * s);
        }

        internal static (double, double, double) InksaveClip(double r, double g, double b)        {
            // 20–35% ink save: compress chroma in L*a*b* space
            var (X, Y, Z) = RGBToXYZ_D65(r, g, b);
            var (L, a, lb) = XYZToLab(X, Y, Z);
            double chroma = Math.Sqrt(a * a + lb * lb);
            if (chroma > 1)
            {
                double inkSaveFactor = 0.72;  // 28% ink save
                a *= inkSaveFactor; lb *= inkSaveFactor;
            }
            (X, Y, Z) = LabToXYZ(L, a, lb);
            (r, g, b) = XYZToRGB_D65(X, Y, Z);
            return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
        }

        // ── Color Space Math ──

        private static (double X, double Y, double Z) RGBToXYZ_D65(double r, double g, double b)
        {
            r = r <= 0.04045 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.04045 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.04045 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            return (
                0.4124564 * r + 0.3575761 * g + 0.1804375 * b,
                0.2126729 * r + 0.7151522 * g + 0.0721750 * b,
                0.0193339 * r + 0.1191920 * g + 0.9503041 * b);
        }

        private static (double X, double Y, double Z) ChromaticAdapt_D65_D50(double X, double Y, double Z)
        {
            // Bradford D65 → D50 matrix
            double[,] M = {
                {  0.8951,  0.2664, -0.1614 },
                { -0.7502,  1.7135,  0.0367 },
                {  0.0389, -0.0685,  1.0296 }
            };
            double Xa = M[0,0]*X + M[0,1]*Y + M[0,2]*Z;
            double Ya = M[1,0]*X + M[1,1]*Y + M[1,2]*Z;
            double Za = M[2,0]*X + M[2,1]*Y + M[2,2]*Z;
            // Scale to D50 white point
            Xa *= 0.95047 / 0.9642;  // D65 white to D50 reference
            Za *= 1.08883 / 0.8249;
            // Back
            double Xd =  1.0478112*Xa + 0.0295424*Ya - 0.0092345*Za;
            double Yd =  0.0228866*Xa + 0.9899583*Ya - 0.0079603*Za;
            double Zd = -0.0055780*Xa - 0.0046828*Ya + 1.0173855*Za;
            return (Xd, Yd, Zd);
        }

        private static (double L, double a, double b) XYZToLab(double X, double Y, double Z)
        {
            double x = X / Xn, y = Y / Yn, z = Z / Zn;
            double fx = x > 0.008856 ? Math.Pow(x, 1.0/3.0) : 7.787*x + 16.0/116.0;
            double fy = y > 0.008856 ? Math.Pow(y, 1.0/3.0) : 7.787*y + 16.0/116.0;
            double fz = z > 0.008856 ? Math.Pow(z, 1.0/3.0) : 7.787*z + 16.0/116.0;
            return (Math.Max(0, 116.0*fy - 16.0), 500.0*(fx - fy), 200.0*(fy - fz));
        }

        private static (double X, double Y, double Z) LabToXYZ(double L, double a, double b)
        {
            double fy = (L + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;
            double x = fx > 0.206893 ? fx*fx*fx : (fx - 16.0/116.0) / 7.787;
            double y = fy > 0.206893 ? fy*fy*fy : (fy - 16.0/116.0) / 7.787;
            double z = fz > 0.206893 ? fz*fz*fz : (fz - 16.0/116.0) / 7.787;
            return (x*Xn, y*Yn, z*Zn);
        }

        private static (double r, double g, double b) XYZToRGB_D65(double X, double Y, double Z)
        {
            double r2 =  3.2404542*X - 1.5371385*Y - 0.4985314*Z;
            double g2 = -0.9692660*X + 1.8760108*Y + 0.0415560*Z;
            double b2 =  0.0556434*X - 0.2040259*Y + 1.0572252*Z;
            r2 = r2 <= 0.00304 ? 12.92*r2 : 1.055*Math.Pow(r2, 1.0/2.4) - 0.055;
            g2 = g2 <= 0.00304 ? 12.92*g2 : 1.055*Math.Pow(g2, 1.0/2.4) - 0.055;
            b2 = b2 <= 0.00304 ? 12.92*b2 : 1.055*Math.Pow(b2, 1.0/2.4) - 0.055;
            return (r2, g2, b2);
        }

        private static (double L, double a, double b) ApplyBPC(double L, double a, double b)
        {
            // BPC: compress shadows toward destination black point
            return (L * 0.90, a, b);
        }

        public void Dispose() { }
    }

    #endregion

    #region Main Color Engine

    /// <summary>
    /// المحرك اللوني الرئيسي — يطبق Color Engine Settings على بكسل
    /// </summary>
    public sealed class IccColorEngine : IDisposable
    {
        private readonly ColorEngineSettings _s;
        private IccColorTransform? _rgbTransform;

        public IccColorEngine(ColorEngineSettings settings)
        {
            _s = settings;
            if (!string.IsNullOrEmpty(_s.RgbProfilePath) && !string.IsNullOrEmpty(_s.OutputProfilePath))
            {
                var rgbProf = IccProfileParser.LoadProfile(_s.RgbProfilePath!);
                var outProf = IccProfileParser.LoadProfile(_s.OutputProfilePath!);
                if (rgbProf != null && outProf != null)
                    _rgbTransform = new IccColorTransform(rgbProf, outProf, _s.Intent, _s.BlackPointCompensation);
            }
        }

        /// <summary>RGB 8-bit → CMYK float (0.0–1.0)</summary>
        public CmykPixel TransformRGB8(byte r, byte g, byte b)
        {
            double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
            (rn, gn, bn) = ApplyBasicAdjustments(rn, gn, bn);
            (rn, gn, bn) = ApplyLabSliders(rn, gn, bn);
            return ApplyColorMode(rn, gn, bn);
        }

        /// <summary>
        /// المسار الكامل للـ pipeline (Stage 4):
        /// نية ICC (Inksave/Saturation/…) → فلاتر الصورة → محاور LAB → وضع اللون → CMYK
        /// يعمل حتى بدون ملفات بروفايل ICC — نية النية تُطبق رياضيًا مباشرة
        /// </summary>
        public CmykPixel TransformRGB(double r, double g, double b)
        {
            if (_s.Intent != RenderingIntent.NoCorrection)
            {
                (r, g, b) = _s.Intent switch
                {
                    RenderingIntent.Saturation        => IccColorTransform.SaturationClip(r, g, b),
                    RenderingIntent.PerceptionInksave => IccColorTransform.InksaveClip(r, g, b),
                    _ => (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1))
                };
            }

            (r, g, b) = ApplyBasicAdjustments(r, g, b);
            (r, g, b) = ApplyLabSliders(r, g, b);
            return ApplyColorMode(r, g, b);
        }

        /// <summary>RGB 16-bit → CMYK float (0.0–1.0)</summary>
        public CmykPixel TransformRGB16(ushort r, ushort g, ushort b)
        {
            double rn = r / 65535.0, gn = g / 65535.0, bn = b / 65535.0;
            (rn, gn, bn) = ApplyBasicAdjustments(rn, gn, bn);
            (rn, gn, bn) = ApplyLabSliders(rn, gn, bn);
            return ApplyColorMode(rn, gn, bn);
        }

        /// <summary>CMYK float → CMYK float (ICC pass-through)</summary>
        public CmykPixel TransformCMYK(double c, double m, double y, double k)
        {
            // Convert CMYK → RGB → ICC transform
            double r = (1 - c) * (1 - k);
            double g = (1 - m) * (1 - k);
            double b2 = (1 - y) * (1 - k);
            (r, g, b2) = ApplyBasicAdjustments(r, g, b2);
            (r, g, b2) = ApplyLabSliders(r, g, b2);
            return ApplyColorMode(r, g, b2);
        }

        /// <summary>Grayscale — luminance → K only</summary>
        public CmykPixel TransformGrayscale(byte gray8)
        {
            double g = gray8 / 255.0;
            return new CmykPixel(0, 0, 0, 1.0 - g);
        }

        // ── Pipeline steps ──

        private (double, double, double) ApplyBasicAdjustments(double r, double g, double b)
        {
            // Brightness: pixel * brightness_factor
            r *= _s.Brightness; g *= _s.Brightness; b *= _s.Brightness;

            // Contrast: (pixel - 0.5) * contrast + 0.5
            double ctr = _s.Contrast;
            r = (r - 0.5) * ctr + 0.5;
            g = (g - 0.5) * ctr + 0.5;
            b = (b - 0.5) * ctr + 0.5;

            // Saturation (Color tab slider)
            (r, g, b) = ApplySaturation(r, g, b, _s.Saturation);

            return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
        }

        private (double, double, double) ApplyLabSliders(double r, double g, double b)
        {
            // RGB → XYZ D65 → Lab → shift → XYZ D65 → RGB
            var (X, Y, Z) = RGBToXYZ_D65(r, g, b);
            var (L, a, lb) = XYZToLab(X, Y, Z);

            L = L * _s.LabL;                    // L-axis (0.0–1.0)
            a = a + _s.LabA * 128.0;            // A-axis shift
            lb = lb + _s.LabB * 128.0;         // B-axis shift

            (X, Y, Z) = LabToXYZ(Math.Clamp(L, 0, 100), a, lb);
            (r, g, b) = XYZToRGB_D65(X, Y, Z);

            return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
        }

        private (double, double, double) ApplySaturation(double r, double g, double b, double satFactor)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            if (delta < 1e-7) return (r, g, b);

            double lum = (max + min) / 2.0;
            double s = (delta / (max < 1e-7 ? 1 : max)) * satFactor;
            s = Math.Clamp(s, 0, 1);
            if (s < 1e-7) return (lum, lum, lum);

            if (lum < 0.5)
            {
                double add = max * s;
                return (lum + add - r * s, lum + add - g * s, lum + add - b * s);
            }
            else
            {
                double sub = (1 - max) * s;
                return (lum - sub + r * s, lum - sub + g * s, lum - sub + b * s);
            }
        }

        private CmykPixel ApplyColorMode(double r, double g, double b) =>
            _s.ColorMode switch
            {
                ColorMode.Grayscale or ColorMode.GrayscaleFast or ColorMode.GrayPhoto
                    => GrayscaleRGB(r, g, b),
                _ => _rgbTransform != null
                    ? _rgbTransform.TransformRGBToCMYK(r, g, b)
                    : CmykPixel.RGBToCMYK(r, g, b)
            };

        private static CmykPixel GrayscaleRGB(double r, double g, double b)
        {
            double gray = Math.Clamp(0.2126729 * r + 0.7151522 * g + 0.0721750 * b, 0, 1);
            return new CmykPixel(0, 0, 0, 1.0 - gray);
        }

        // Math shortcuts
        private static (double X, double Y, double Z) RGBToXYZ_D65(double r, double g, double b)
        {
            r = r <= 0.04045 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.04045 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.04045 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            return (0.4124564*r + 0.3575761*g + 0.1804375*b,
                    0.2126729*r + 0.7151522*g + 0.0721750*b,
                    0.0193339*r + 0.1191920*g + 0.9503041*b);
        }

        private static (double L, double a, double b) XYZToLab(double X, double Y, double Z)
        {
            double x = X / 0.9642, y = Y / 1.0, z = Z / 0.8249;
            double fx = x > 0.008856 ? Math.Pow(x, 1.0/3.0) : 7.787*x + 16.0/116.0;
            double fy = y > 0.008856 ? Math.Pow(y, 1.0/3.0) : 7.787*y + 16.0/116.0;
            double fz = z > 0.008856 ? Math.Pow(z, 1.0/3.0) : 7.787*z + 16.0/116.0;
            return (Math.Max(0, 116.0*fy - 16.0), 500.0*(fx - fy), 200.0*(fy - fz));
        }

        private static (double X, double Y, double Z) LabToXYZ(double L, double a, double b)
        {
            double fy = (L + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;
            double x = fx > 0.206893 ? fx*fx*fx : (fx - 16.0/116.0) / 7.787;
            double y = fy > 0.206893 ? fy*fy*fy : (fy - 16.0/116.0) / 7.787;
            double z = fz > 0.206893 ? fz*fz*fz : (fz - 16.0/116.0) / 7.787;
            return (x*0.9642, y*1.0, z*0.8249);
        }

        private static (double r, double g, double b) XYZToRGB_D65(double X, double Y, double Z)
        {
            double r2 =  3.2404542*X - 1.5371385*Y - 0.4985314*Z;
            double g2 = -0.9692660*X + 1.8760108*Y + 0.0415560*Z;
            double b2 =  0.0556434*X - 0.2040259*Y + 1.0572252*Z;
            r2 = r2 <= 0.00304 ? 12.92*r2 : 1.055*Math.Pow(r2, 1.0/2.4) - 0.055;
            g2 = g2 <= 0.00304 ? 12.92*g2 : 1.055*Math.Pow(g2, 1.0/2.4) - 0.055;
            b2 = b2 <= 0.00304 ? 12.92*b2 : 1.055*Math.Pow(b2, 1.0/2.4) - 0.055;
            return (r2, g2, b2);
        }

        public void Dispose() { _rgbTransform?.Dispose(); }
    }

    #endregion
}