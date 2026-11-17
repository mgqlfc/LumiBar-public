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

    public static NeonAlbumPalette DeriveAlbumNeonPalette(Color accent, string? secondaryAccentHex = null)
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

        double primarySat = Math.Clamp(Math.Max(s * 1.25, 0.90), 0.88, 0.98);
        double primaryLit = Math.Clamp(l, 0.52, 0.62);

        Color primary = HslToRgb(h, primarySat, primaryLit);

        // Synthwave dual-tone separation: avoid monochrome (< 55deg) shifts
        // Warm hues (red/orange/gold) pair toward electric magenta/cyan-violet; cool hues pair toward violet/magenta
        double defaultSecHue = (h < 70 || h >= 305)
            ? (h - 72.0 + 360.0) % 360.0   // Tangerine/Red -> Hyper Magenta/Violet
            : (h + 95.0) % 360.0;          // Cyan/Emerald -> Violet/Magenta

        Color secondary = HslToRgb(defaultSecHue, Math.Max(0.88, primarySat - 0.02), 0.57);
        if (!string.IsNullOrWhiteSpace(secondaryAccentHex))
        {
            var rawSec = Sdk.WeatherFormatting.ParseHexColor(secondaryAccentHex, secondary);
            var (sh, ss, sl) = RgbToHsl(rawSec);
            double hueDiff = Math.Abs(h - sh);
            if (hueDiff > 180.0) hueDiff = 360.0 - hueDiff;
            if (ss >= 0.16 && sl is >= 0.10 and <= 0.92 && hueDiff >= 52.0)
                secondary = HslToRgb(sh, Math.Clamp(Math.Max(ss * 1.25, 0.88), 0.86, 0.98), Math.Clamp(sl, 0.52, 0.62));
        }

        Color highlight = HslToRgb((h - 24.0 + 360.0) % 360.0, 0.94, 0.68);
        return new NeonAlbumPalette(primary, secondary, highlight);
    }

    public static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    /// Applies the B1 80s Hi-Fi brushed-gunmetal bevel key finish to transport/control buttons,
    /// overriding WinUI Button visual state brushes so hover/pressed states keep exact hardware material.
    /// </summary>
    public static void ApplyB1KeyStyle(Control btn, bool isPrimary, bool isDark)
    {
        _ = isDark; // B1 Hi-Fi hardware console always retains its dark brushed gunmetal / burnt bronze chassis finish

        LinearGradientBrush BuildVerticalBrush(Color top, Color mid, Color bot) => new()
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Color = top, Offset = 0.0 },
                new GradientStop { Color = mid, Offset = 0.50 },
                new GradientStop { Color = bot, Offset = 1.0 }
            }
        };

        if (isPrimary)
        {
            var bg = BuildVerticalBrush(Color.FromArgb(255, 58, 38, 28), Color.FromArgb(255, 36, 22, 18), Color.FromArgb(255, 23, 14, 12));
            var bgHover = BuildVerticalBrush(Color.FromArgb(255, 74, 48, 34), Color.FromArgb(255, 48, 29, 23), Color.FromArgb(255, 30, 18, 15));
            var bgPress = BuildVerticalBrush(Color.FromArgb(255, 34, 21, 16), Color.FromArgb(255, 22, 13, 11), Color.FromArgb(255, 16, 10, 9));
            var border = BuildVerticalBrush(Color.FromArgb(148, 255, 183, 77), Color.FromArgb(245, 9, 13, 22), Color.FromArgb(255, 6, 8, 14));
            var fg = new SolidColorBrush(Color.FromArgb(255, 255, 209, 128));

            btn.Background = bg;
            btn.BorderBrush = border;
            btn.Foreground = fg;
            SetButtonStateBrushes(btn, bg, bgHover, bgPress, border, border, fg);
            return;
        }

        var kBg = BuildVerticalBrush(Color.FromArgb(255, 43, 53, 75), Color.FromArgb(255, 25, 32, 48), Color.FromArgb(255, 16, 21, 34));
        var kBgHover = BuildVerticalBrush(Color.FromArgb(255, 56, 68, 96), Color.FromArgb(255, 34, 43, 64), Color.FromArgb(255, 22, 28, 45));
        var kBgPress = BuildVerticalBrush(Color.FromArgb(255, 24, 30, 44), Color.FromArgb(255, 16, 20, 32), Color.FromArgb(255, 11, 14, 24));
        var kBorder = BuildVerticalBrush(Color.FromArgb(88, 255, 255, 255), Color.FromArgb(245, 9, 13, 22), Color.FromArgb(255, 6, 8, 14));
        var kFg = new SolidColorBrush(Color.FromArgb(255, 186, 230, 253));

        btn.Background = kBg;
        btn.BorderBrush = kBorder;
        btn.Foreground = kFg;
        SetButtonStateBrushes(btn, kBg, kBgHover, kBgPress, kBorder, kBorder, kFg);
    }

    private static void SetButtonStateBrushes(
        Control btn,
        Brush normalBg,
        Brush hoverBg,
        Brush pressBg,
        Brush border,
        Brush hoverBorder,
        Brush fg)
    {
        btn.Resources["ButtonBackground"] = normalBg;
        btn.Resources["ButtonBackgroundPointerOver"] = hoverBg;
        btn.Resources["ButtonBackgroundPressed"] = pressBg;
        btn.Resources["ButtonBackgroundDisabled"] = normalBg;
        btn.Resources["ButtonBorderBrush"] = border;
        btn.Resources["ButtonBorderBrushPointerOver"] = hoverBorder;
        btn.Resources["ButtonBorderBrushPressed"] = border;
        btn.Resources["ButtonBorderBrushDisabled"] = border;
        btn.Resources["ButtonForeground"] = fg;
        btn.Resources["ButtonForegroundPointerOver"] = fg;
        btn.Resources["ButtonForegroundPressed"] = fg;
        btn.Resources["ButtonForegroundDisabled"] = new SolidColorBrush(Color.FromArgb(110, 148, 163, 184));
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
