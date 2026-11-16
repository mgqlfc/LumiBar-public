using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace AntigravityFrontend.Controls;

/// <summary>
/// Renders a 1984 DeLorean-style segmented VFD brick spectrum (14x8 in flyout, 8x5 in compact/lite capsule)
/// with Cyan -> Amber-Orange -> Hyper-Magenta brick tiers, peak hold caps, and smoothed level readout.
/// </summary>
public sealed class SpectrumBackdropControl : Canvas
{
    private const int FullCols = 14;
    private const int FullRows = 8;
    private const int CompactCols = 8;
    private const int CompactRows = 5;

    private readonly Rectangle[,] _cells = new Rectangle[FullCols, FullRows];
    private readonly Rectangle _tlH = new();
    private readonly Rectangle _tlV = new();
    private readonly Rectangle _trH = new();
    private readonly Rectangle _trV = new();

    private readonly double[] _smoothed = new double[FullCols];
    private readonly double[] _peaks = new double[FullCols];
    private readonly int[] _peakHold = new int[FullCols];

    private SolidColorBrush _cyanBrush = new(Color.FromArgb(255, 0, 245, 212));
    private SolidColorBrush _amberBrush = new(Color.FromArgb(255, 255, 158, 0));
    private SolidColorBrush _magentaBrush = new(Color.FromArgb(255, 255, 42, 133));
    private SolidColorBrush _dimCellBrush = new(Color.FromArgb(24, 148, 163, 184));
    private SolidColorBrush _bracketBrush = new(Color.FromArgb(62, 0, 245, 212));

    private double _opacityScale = 0.85;
    private bool _isPlaying;
    private bool _isEnabled = true;
    private bool _compactMode;
    private double _sensitivity = 1.0;

    public IReadOnlyList<double> CurrentLevels => _smoothed;

    public SpectrumBackdropControl()
    {
        IsHitTestVisible = false;

        foreach (var b in new[] { _tlH, _tlV, _trH, _trV })
        {
            b.Fill = _bracketBrush;
            Children.Add(b);
        }

        for (int c = 0; c < FullCols; c++)
        {
            for (int r = 0; r < FullRows; r++)
            {
                var cell = new Rectangle
                {
                    RadiusX = 1.0,
                    RadiusY = 1.0,
                    Fill = _dimCellBrush,
                    Opacity = 0.72
                };
                _cells[c, r] = cell;
                Children.Add(cell);
            }
        }

        SizeChanged += (_, _) => LayoutBricks();
    }

    public void Configure(
        Color accent,
        bool isPlaying,
        bool isEnabled,
        double sensitivity,
        double opacityScale,
        bool compactMode = false)
    {
        _ = accent; // Brick palette stays classic VFD Cyan / Amber / Magenta per DeLorean spec
        bool modeChanged = _compactMode != compactMode;
        _isPlaying = isPlaying;
        _isEnabled = isEnabled;
        _compactMode = compactMode;
        _sensitivity = Math.Clamp(sensitivity, 0.5, 2.5);
        _opacityScale = Math.Clamp(opacityScale, 0.25, 1.0);
        Opacity = _isEnabled ? _opacityScale : 0.0;
        if (modeChanged)
            LayoutBricks();
    }

    public void UpdateBands(double[]? bands20)
    {
        if (!_isEnabled || ActualWidth <= 4 || ActualHeight <= 4) return;

        int cols = _compactMode ? CompactCols : FullCols;
        for (int c = 0; c < cols; c++)
        {
            double target = 0.03;
            if (_isPlaying && bands20 != null && bands20.Length > 0)
            {
                double pos = (double)c / Math.Max(1, cols - 1) * (bands20.Length - 1);
                int idx0 = (int)Math.Floor(pos);
                int idx1 = Math.Min(bands20.Length - 1, idx0 + 1);
                double frac = pos - idx0;
                double raw = bands20[idx0] * (1 - frac) + bands20[idx1] * frac;
                // Gentle boost so musical peaks genuinely reach the top VFD ceiling
                target = Math.Clamp(Math.Pow(raw * _sensitivity, 0.85) * 1.12, 0.08, 1.0);
            }

            // Conservative automotive VFD damping (smooth attack, measured decay)
            double factor = target > _smoothed[c] ? 0.28 : 0.11;
            _smoothed[c] += (target - _smoothed[c]) * factor;

            if (_smoothed[c] >= _peaks[c])
            {
                _peaks[c] = _smoothed[c];
                _peakHold[c] = 10;
            }
            else if (_peakHold[c] > 0)
            {
                _peakHold[c]--;
            }
            else
            {
                _peaks[c] = Math.Max(_smoothed[c], _peaks[c] - 0.015);
            }
        }

        LayoutBricks();
    }

    private void LayoutBricks()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        int cols = _compactMode ? CompactCols : FullCols;
        int rows = _compactMode ? CompactRows : FullRows;

        bool showBrackets = !_compactMode;
        _tlH.Visibility = _tlV.Visibility = _trH.Visibility = _trV.Visibility =
            showBrackets ? Visibility.Visible : Visibility.Collapsed;
        if (showBrackets)
        {
            _tlH.Width = 10; _tlH.Height = 1; SetLeft(_tlH, 0); SetTop(_tlH, 0);
            _tlV.Width = 1; _tlV.Height = 6; SetLeft(_tlV, 0); SetTop(_tlV, 0);
            _trH.Width = 10; _trH.Height = 1; SetLeft(_trH, Math.Max(0, w - 10)); SetTop(_trH, 0);
            _trV.Width = 1; _trV.Height = 6; SetLeft(_trV, Math.Max(0, w - 1)); SetTop(_trV, 0);
        }

        double insetX = _compactMode ? 0 : 3;
        double usableW = Math.Max(4, w - insetX * 2);
        double colGap = _compactMode ? 2.0 : 4.5;
        double rowGap = _compactMode ? 1.5 : 2.6;
        double colW = Math.Max(2.0, (usableW - colGap * (cols - 1)) / cols);
        double segH = Math.Max(1.6, (h - rowGap * (rows - 1)) / rows);

        for (int c = 0; c < FullCols; c++)
        {
            for (int r = 0; r < FullRows; r++)
            {
                var cell = _cells[c, r];
                if (c >= cols || r >= rows)
                {
                    cell.Visibility = Visibility.Collapsed;
                    continue;
                }

                cell.Visibility = Visibility.Visible;
                cell.RadiusX = 0;
                cell.RadiusY = 0;
                double x = insetX + c * (colW + colGap);
                double y = Math.Max(0, h - (r + 1) * segH - r * rowGap);
                cell.Width = colW;
                cell.Height = segH;
                SetLeft(cell, x);
                SetTop(cell, y);

                int litRows = (int)Math.Round(_smoothed[c] * rows);
                int peakRow = Math.Clamp((int)Math.Round(_peaks[c] * rows) - 1, 0, rows - 1);
                bool isLit = r < litRows;
                bool isPeakCap = !_compactMode && _isPlaying && _peaks[c] > 0.14 && r == peakRow;
                double frac = (double)(r + 1) / rows;

                if (isLit || isPeakCap)
                {
                    cell.Fill = frac > 0.78 ? _magentaBrush : (frac > 0.45 ? _amberBrush : _cyanBrush);
                    cell.Opacity = isPeakCap && !isLit ? 0.94 : (_compactMode ? 0.92 : 0.80);
                }
                else
                {
                    cell.Fill = _dimCellBrush;
                    cell.Opacity = _compactMode ? 0.72 : 0.76;
                }
            }
        }
    }
}
