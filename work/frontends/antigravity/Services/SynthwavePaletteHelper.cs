using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AntigravityFrontend.Services;

/// <summary>
/// Derives harmonized high-saturation Synthwave neon palettes from album accent colors
/// and provides shared B1 Hi-Fi brushed-gunmetal hardware key styling.
/// </summary>
public static class SynthwavePaletteHelper
{
    public readonly record struct NeonAlbumPalette(Color Primary, Color Secondary, Color Highlight);

    public static NeonAlbumPalette DeriveAlbumNeonPalette(Color accent)
    {
        var (h, s, l) = RgbToHsl(accent);
        if (s < 0.14 || (l is < 0.08 or > 0.94))
        {
            // Fallback for grayscale album art: classic Outrun Tangerine -> Magenta -> Solar Gold
            return new NeonAlbumPalette(
                Color.FromArgb(255, 255, 107, 0),
                Color.FromArgb(255, 255, 42, 133),
                Color.FromArgb(255, 253, 224, 71));
        }

        double primarySat = Math.Clamp(Math.Max(s * 1.25, 0.88), 0.86, 0.98);
        double primaryLit = Math.Clamp(l, 0.52, 0.62);

        Color primary = HslToRgb(h, primarySat, primaryLit);
        Color secondary = HslToRgb((h + 46.0) % 360.0, Math.Max(0.86, primarySat - 0.04), 0.56);
        Color highlight = HslToRgb((h - 26.0 + 360.0) % 360.0, 0.92, 0.68);
        return new NeonAlbumPalette(primary, secondary, highlight);
    }

    public static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    /// Applies the B1 80s Hi-Fi brushed-gunmetal bevel key finish to transport/control buttons.
    /// </summary>
    public static void ApplyB1KeyStyle(Control btn, bool isPrimary, bool isDark)
    {
        if (isPrimary)
        {
            btn.Background = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = isDark ? Color.FromArgb(255, 62, 38, 26) : Color.FromArgb(255, 234, 88, 12), Offset = 0.0 },
                    new GradientStop { Color = isDark ? Color.FromArgb(255, 36, 22, 18) : Color.FromArgb(255, 194, 65, 12), Offset = 0.52 },
                    new GradientStop { Color = isDark ? Color.FromArgb(255, 22, 13, 11) : Color.FromArgb(255, 154, 52, 18), Offset = 1.0 }
                }
            };
            btn.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(215, 255, 168, 76), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(220, 12, 8, 10), Offset = 0.32 },
                    new GradientStop { Color = Color.FromArgb(245, 7, 9, 14), Offset = 1.0 }
                }
            };
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromArgb(255, 255, 209, 128) : Color.FromArgb(255, 255, 247, 237));
            return;
        }

        btn.Background = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Color = isDark ? Color.FromArgb(255, 43, 53, 75) : Color.FromArgb(255, 241, 245, 249), Offset = 0.0 },
                new GradientStop { Color = isDark ? Color.FromArgb(255, 25, 32, 48) : Color.FromArgb(255, 226, 232, 240), Offset = 0.50 },
                new GradientStop { Color = isDark ? Color.FromArgb(255, 16, 21, 34) : Color.FromArgb(255, 203, 213, 225), Offset = 1.0 }
            }
        };
        btn.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(isDark ? (byte)84 : (byte)220, 255, 255, 255), Offset = 0.0 },
                new GradientStop { Color = Color.FromArgb(220, 9, 13, 22), Offset = 0.35 },
                new GradientStop { Color = Color.FromArgb(240, 7, 10, 18), Offset = 1.0 }
            }
        };
        btn.Foreground = new SolidColorBrush(isDark ? Color.FromArgb(255, 186, 230, 253) : Color.FromArgb(255, 30, 41, 59));
    }

    private static (double H, double S, double L) RgbToHsl(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) * 0.5;

        if (Math.Abs(max - min) < 1e-5)
            return (0, 0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 1e-5)
            h = (g - b) / d + (g < b ? 6.0 : 0.0);
        else if (Math.Abs(max - g) < 1e-5)
            h = (b - r) / d + 2.0;
        else
            h = (r - g) / d + 4.0;

        return (h * 60.0, s, l);
    }

    private static Color HslToRgb(double hDeg, double s, double l)
    {
        double h = ((hDeg % 360.0) + 360.0) % 360.0 / 360.0;
        if (s <= 0.0)
        {
            byte v = (byte)Math.Round(l * 255.0);
            return Color.FromArgb(255, v, v, v);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;
        byte r = (byte)Math.Round(HueToChannel(p, q, h + 1.0 / 3.0) * 255.0);
        byte g = (byte)Math.Round(HueToChannel(p, q, h) * 255.0);
        byte b = (byte)Math.Round(HueToChannel(p, q, h - 1.0 / 3.0) * 255.0);
        return Color.FromArgb(255, r, g, b);
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }
}
