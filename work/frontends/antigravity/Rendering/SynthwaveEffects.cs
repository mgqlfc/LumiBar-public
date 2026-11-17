using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace AntigravityFrontend.Rendering;

/// <summary>Build in CreateResources/resize; cache the returned graphs between Draw calls.</summary>
public static class SynthwaveEffects
{
    public static GaussianBlurEffect Glow(ICanvasImage source, float amount = 5) => new()
    {
        Source = source, BlurAmount = amount, BorderMode = EffectBorderMode.Soft,
        Optimization = EffectOptimization.Quality
    };

    public static BlendEffect Screen(ICanvasImage background, ICanvasImage foreground) => new()
    { Background = background, Foreground = foreground, Mode = BlendEffectMode.Screen };

    // Win2D composites consume destination first, source second.
    public static CompositeEffect Mask(ICanvasImage destination, ICanvasImage alphaMask) =>
        Composite(destination, alphaMask, CanvasComposite.DestinationIn);

    public static CompositeEffect PunchOut(ICanvasImage destination, ICanvasImage scanlines) =>
        Composite(destination, scanlines, CanvasComposite.DestinationOut);

    private static CompositeEffect Composite(ICanvasImage destination, ICanvasImage source, CanvasComposite mode)
    {
        var effect = new CompositeEffect { Mode = mode };
        effect.Sources.Add(destination); effect.Sources.Add(source); return effect;
    }

    public static CompositeEffect Reflection(ICanvasImage bricks, Rect bottomRows, float mirrorAxisY, ICanvasImage fresnelMask)
    {
        var cropped = new CropEffect { Source = bricks, SourceRectangle = bottomRows };
        var mirrored = new Transform2DEffect
        {
            Source = cropped,
            TransformMatrix = Matrix3x2.CreateScale(1, -1) * Matrix3x2.CreateTranslation(0, 2 * mirrorAxisY)
        };
        return Mask(Glow(mirrored, 3.2f), fresnelMask);
    }

    public static CanvasLinearGradientBrush LyricGradient(ICanvasResourceCreator device, float height) => new(device,
        [new() { Position = 0, Color = Colors.White },
         new() { Position = .55f, Color = global::Windows.UI.Color.FromArgb(255, 0x67, 0xE8, 0xF9) },
         new() { Position = 1, Color = global::Windows.UI.Color.FromArgb(255, 0xFD, 0xE6, 0x8A) }])
    { StartPoint = Vector2.Zero, EndPoint = new(0, height) };

    /// <summary>draw: 1 physical pixel out of every 3. Rebuild when DPI/size changes.</summary>
    public static CanvasRenderTarget ScanlineMask(CanvasDevice device, float widthDip, float heightDip, float dpi)
    {
        var mask = new CanvasRenderTarget(device, widthDip, heightDip, dpi);
        using var ds = mask.CreateDrawingSession(); ds.Clear(Colors.Transparent);
        ds.Antialiasing = CanvasAntialiasing.Aliased;
        var pixel = 96f / dpi;
        for (float y = 0; y < heightDip; y += 3 * pixel)
            ds.FillRectangle(0, y, widthDip, pixel, Colors.White);
        return mask;
    }

    /// <summary>Geometry uses DIPs; shift and sliceHeight are physical pixels at the supplied DPI.</summary>
    public static CompositeEffect TearSlice(ICanvasImage text, float width, float height, float y, float shift = 7, float sliceHeight = 7, float dpi = 96)
    {
        shift *= 96 / dpi; sliceHeight *= 96 / dpi;
        y = Math.Clamp(y, 0, height); sliceHeight = Math.Clamp(sliceHeight, 0, height - y);
        var result = new CompositeEffect { Mode = CanvasComposite.SourceOver };
        result.Sources.Add(new CropEffect { Source = text, SourceRectangle = new(0, 0, width, y) });
        result.Sources.Add(new Transform2DEffect
        {
            Source = new CropEffect { Source = text, SourceRectangle = new(0, y, width, sliceHeight) },
            TransformMatrix = Matrix3x2.CreateTranslation(shift, 0)
        });
        result.Sources.Add(new CropEffect { Source = text, SourceRectangle = new(0, y + sliceHeight, width, height - y - sliceHeight) });
        return result;
    }

    /// <summary>
    /// Builds a 96-DPI B8G8R8A8 displacement bitmap encoding a convex CRT barrel curvature centered at (w/2, h/2).
    /// </summary>
    public static CanvasBitmap CreateCrtDisplacementBitmap(CanvasDevice device, int widthDip, int heightDip, float maxShiftDip = 24f, float strength = 0.038f)
    {
        int w = Math.Max(4, widthDip);
        int h = Math.Max(4, heightDip);
        byte[] pixels = new byte[w * h * 4];
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;
        float scaleRange = Math.Max(2f, maxShiftDip * 2f); // Amount = scaleRange -> ±maxShiftDip

        for (int y = 0; y < h; y++)
        {
            float ny = (y - halfH) / halfH;
            int rowBase = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                float nx = (x - halfW) / halfW;
                float r2 = nx * nx + ny * ny;
                float dxDip = nx * halfW * strength * r2;
                float dyDip = ny * halfH * strength * r2;

                byte r = (byte)Math.Clamp((int)MathF.Round((0.5f + dxDip / scaleRange) * 255f), 0, 255);
                byte g = (byte)Math.Clamp((int)MathF.Round((0.5f + dyDip / scaleRange) * 255f), 0, 255);
                int idx = rowBase + x * 4;
                pixels[idx + 0] = 128; // B
                pixels[idx + 1] = g;   // G -> YChannelSelect
                pixels[idx + 2] = r;   // R -> XChannelSelect
                pixels[idx + 3] = 255; // A
            }
        }

        return CanvasBitmap.CreateFromBytes(device, pixels, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f);
    }

    public static DisplacementMapEffect WarpCrt(ICanvasImage source, CanvasBitmap displacementMap, float offsetX = 0f, float offsetY = 0f, float maxShiftDip = 24f)
    {
        ICanvasImage disp = (MathF.Abs(offsetX) > 0.1f || MathF.Abs(offsetY) > 0.1f)
            ? new Transform2DEffect
            {
                Source = displacementMap,
                TransformMatrix = Matrix3x2.CreateTranslation(-offsetX, -offsetY)
            }
            : displacementMap;

        return new DisplacementMapEffect
        {
            Source = source,
            Displacement = disp,
            Amount = maxShiftDip * 2f,
            XChannelSelect = EffectChannelSelect.Red,
            YChannelSelect = EffectChannelSelect.Green
        };
    }
}
