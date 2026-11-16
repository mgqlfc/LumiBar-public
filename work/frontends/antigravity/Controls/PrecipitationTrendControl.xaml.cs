using System;
using System.Collections.Generic;
using System.Linq;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AntigravityFrontend.Controls;

public sealed partial class PrecipitationTrendControl : UserControl
{
    public PrecipitationTrendControl()
    {
        InitializeComponent();
    }

    public void Update(PrecipitationViewModel? model, bool isMockMode, bool isDark)
    {
        RootBorder.Background = new SolidColorBrush(isDark
            ? Color.FromArgb(56, 0, 0, 0)
            : Color.FromArgb(152, 255, 255, 255));
        RootBorder.BorderBrush = Win32WindowHelper.CreateGlassBorderBrush(isDark, 44, 10);

        DemoTag.Visibility = isMockMode ? Visibility.Visible : Visibility.Collapsed;

        var slots = model?.Slots;
        if (slots == null || slots.Count == 0)
        {
            BarsHost.Visibility = Visibility.Collapsed;
            EmptyStateText.Visibility = Visibility.Visible;
            HeadlineText.Text = isMockMode ? "示例空态" : "暂未返回";
            return;
        }

        BarsHost.Visibility = Visibility.Visible;
        EmptyStateText.Visibility = Visibility.Collapsed;

        double maxMm = slots.Where(s => s.Millimeters.HasValue).Select(s => s.Millimeters!.Value).DefaultIfEmpty(0).Max();
        bool incomplete = model != null && !model.Complete;
        HeadlineText.Text = maxMm > 0.01
            ? $"峰值 {maxMm:0.0} mm{(incomplete ? " · 部分缺失" : "")}"
            : $"未来 1h 无明显降水{(incomplete ? " · 部分缺失" : "")}";

        double scaleMax = Math.Max(1.5, maxMm);
        BarsHost.Children.Clear();
        BarsHost.ColumnDefinitions.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            BarsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slot = slots[i];

            var col = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            FrameworkElement barVisual;
            if (slot.IsGap)
            {
                barVisual = new Border
                {
                    Width = 14,
                    Height = 6,
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 148, 163, 184)),
                    Background = new SolidColorBrush(Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                ToolTipService.SetToolTip(barVisual, $"{slot.Label}：数据缺失");
            }
            else
            {
                double mm = slot.Millimeters!.Value;
                var barHeight = mm <= 0.001 ? 4 : Math.Max(6, Math.Min(26, mm / scaleMax * 26.0));
                barVisual = new Border
                {
                    Width = 14,
                    Height = barHeight,
                    CornerRadius = new CornerRadius(4, 4, 2, 2),
                    Background = new SolidColorBrush(mm > 0.01
                        ? Color.FromArgb(230, 56, 189, 248)
                        : Color.FromArgb(85, 148, 163, 184)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                double avgRate = slot.AvgIntensityMmPerHour ?? 0;
                ToolTipService.SetToolTip(barVisual, $"{slot.Label}：区间降水量 {mm:0.00} mm（区间平均强度 {avgRate:0.0} mm/h · 15分钟模型趋势）");
            }

            var lbl = new TextBlock
            {
                Text = slot.Label,
                FontSize = 11,
                Opacity = 0.72,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            col.Children.Add(barVisual);
            col.Children.Add(lbl);
            Grid.SetColumn(col, i);
            BarsHost.Children.Add(col);
        }
    }
}
