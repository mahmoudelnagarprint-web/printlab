using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElnagarPrint.RIP.Core;

namespace ElnagarPrint.RIP.UI.Services;

/// <summary>
/// عارض المعاينة السريعة — يحوّل HalftoneResult إلى BitmapSource
/// • التوليفية: تركيب القنوات الستة في RGB
/// • لكل قناة: صورة رمادية لكثافة النقاط
/// </summary>
public static class HalftonePreviewRenderer
{
    /// <summary>
    /// رسم المعاينة: channelIndex = -1 → توليفية | 0..5 → قناة مفردة
    /// ترتيب القنوات: 0=C 1=M 2=Y 3=K 4=Lc 5=Lm
    /// </summary>
    public static BitmapSource? Render(HalftoneResult? result, int channelIndex = -1)
    {
        if (result?.Cyan is null || result.Width <= 0 || result.Height <= 0)
            return null;

        int w = result.Width;
        int h = result.Height;
        var pixels = new byte[w * h * 3];

        var channels = new[] { result.Cyan, result.Magenta, result.Yellow, result.Black, result.LightCyan, result.LightMagenta };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 3;

                if (channelIndex >= 0 && channelIndex < 6)
                {
                    // قناة مفردة — رمادي
                    byte lv = channels[channelIndex].DotMap[y * w + x];
                    byte ink = (byte)(lv * 85); // 0/85/170/255
                    byte value = (byte)(255 - ink); // الحبر أسود على خلفية بيضاء
                    pixels[idx] = value;
                    pixels[idx + 1] = value;
                    pixels[idx + 2] = value;
                }
                else
                {
                    // توليفية: اطرح الحبر من الأبيض (فرض فصل Naive)
                    double c = channels[0].DotMap[y * w + x] / 3.0;
                    double m = channels[1].DotMap[y * w + x] / 3.0;
                    double yy = channels[2].DotMap[y * w + x] / 3.0;
                    double k = channels[3].DotMap[y * w + x] / 3.0;

                    // تقدير: R = 1 - (C*0.9 + K) ...
                    byte r = ToByte255(1.0 - Math.Clamp(c * 0.9 + k, 0, 1));
                    byte g = ToByte255(1.0 - Math.Clamp(m * 0.9 + k, 0, 1));
                    byte b = ToByte255(1.0 - Math.Clamp(yy * 0.9 + k, 0, 1));
                    pixels[idx]     = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                }
            }
        }

        var bmp = BitmapSource.Create(
            w, h, 96, 96, PixelFormats.Rgb24, null, pixels, w * 3);
        bmp.Freeze();
        return bmp;
    }

    private static byte ToByte255(double v) => (byte)(Math.Clamp(v, 0, 1) * 255);

    /// <summary>تكبير الصورة الصغيرة لتناسب مساحة المعاينة (Nearest Neighbor للحفاظ على النقاط)</summary>
    public static TransformedBitmap? Upscale(BitmapSource? source, int targetW = 320)
    {
        if (source is null) return null;
        double scale = Math.Max(1, targetW / (double)source.PixelWidth);
        var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        tb.Freeze();
        return tb;
    }
}