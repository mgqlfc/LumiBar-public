using System.Numerics;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;

namespace AntigravityFrontend.Rendering;

internal static class GraphicsSelfTest
{
    internal static async void Start(App app, string output)
    {
        var checks = new Dictionary<string, bool>(); var exit = 1;
        Directory.CreateDirectory(output);
        try
        {
            var canvas = new CanvasControl { ClearColor = Colors.Transparent };
            checks["CanvasControl"] = canvas != null; canvas!.RemoveFromVisualTree();
            using var device = new CanvasDevice();
            CanvasRenderTarget Target() => new(device, 64, 64, 96);
            using var text = Target();
            using (var draw = text.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); draw.FillRectangle(16, 16, 32, 32, Colors.White); }
            using var scan = SynthwaveEffects.ScanlineMask(device, 64, 64, 96);
            using var punched = Target();
            using (var draw = punched.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var cut = SynthwaveEffects.PunchOut(text, scan); draw.DrawImage(cut); }
            var pixels = punched.GetPixelBytes();
            byte Alpha(int x, int y) => pixels[(y * 64 + x) * 4 + 3];
            checks["DestinationOut scanline transparency"] = Alpha(32, 18) == 0 && Alpha(32, 19) == 255;
            await punched.SaveAsync(Path.Combine(output, "scanline-alpha.png"), CanvasBitmapFileFormat.Png);
            using var blurred = Target();
            using (var draw = blurred.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var glow = SynthwaveEffects.Glow(text, 5); draw.DrawImage(glow); }
            checks["GaussianBlurEffect 5px halo"] = blurred.GetPixelBytes()[(32 * 64 + 12) * 4 + 3] > 0;
            using var red = Target(); using var blue = Target(); using var screened = Target();
            using (var draw = red.CreateDrawingSession()) draw.Clear(global::Windows.UI.Color.FromArgb(255, 128, 0, 0));
            using (var draw = blue.CreateDrawingSession()) draw.Clear(global::Windows.UI.Color.FromArgb(255, 0, 0, 128));
            using (var draw = screened.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var screen = SynthwaveEffects.Screen(red, blue); draw.DrawImage(screen); }
            var screenPixels = screened.GetPixelBytes();
            checks["BlendEffectMode.Screen"] = screenPixels[0] >= 120 && screenPixels[2] >= 120;
            using var mask = Target(); using var faded = Target();
            using (var draw = mask.CreateDrawingSession())
            {
                draw.Clear(Colors.Transparent);
                using var gradient = new CanvasLinearGradientBrush(device, Colors.White, Colors.Transparent)
                { StartPoint = new(0, 16), EndPoint = new(0, 48) };
                draw.FillRectangle(0, 0, 64, 64, gradient);
            }
            using (var draw = faded.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var effect = SynthwaveEffects.Mask(text, mask); draw.DrawImage(effect); }
            var fadePixels = faded.GetPixelBytes();
            checks["DestinationIn Fresnel fade"] = fadePixels[(20 * 64 + 32) * 4 + 3] > fadePixels[(44 * 64 + 32) * 4 + 3];
            using var reflection = Target();
            using (var draw = reflection.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var effect = SynthwaveEffects.Reflection(text, new(16, 40, 32, 8), 40, mask); draw.DrawImage(effect); }
            checks["3.2px mirrored reflection"] = reflection.GetPixelBytes().Where((_, i) => i % 4 == 3).Any(x => x > 0);
            using var torn = Target();
            using (var draw = torn.CreateDrawingSession())
            { draw.Clear(Colors.Transparent); using var effect = SynthwaveEffects.TearSlice(text, 64, 64, 24); draw.DrawImage(effect); }
            var tearPixels = torn.GetPixelBytes();
            checks["7px VHS slice"] = tearPixels[(26 * 64 + 17) * 4 + 3] == 0 && tearPixels[(26 * 64 + 24) * 4 + 3] == 255;
            using var gradientText = Target();
            using (var draw = gradientText.CreateDrawingSession())
            {
                draw.Clear(Colors.Transparent); using var brush = SynthwaveEffects.LyricGradient(device, 48);
                using var format = new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 18 };
                using var layout = new CanvasTextLayout(device, "VFD", format, 64, 64);
                draw.DrawTextLayout(layout, Vector2.Zero, brush);
            }
            checks["Gradient CanvasTextLayout"] = gradientText.GetPixelBytes().Where((_, i) => i % 4 == 3).Any(x => x > 0);
            await gradientText.SaveAsync(Path.Combine(output, "gradient-text.png"), CanvasBitmapFileFormat.Png);
            exit = checks.Values.All(x => x) ? 0 : 1;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); }
        finally
        {
            File.WriteAllText(Path.Combine(output, "graphics-result.json"), JsonSerializer.Serialize(new { pass = exit == 0, checks }, new JsonSerializerOptions { WriteIndented = true }));
            app.Exit(); Environment.Exit(exit);
        }
    }
}
