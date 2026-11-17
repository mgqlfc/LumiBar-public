using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AntigravityFrontend.Sdk;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using WinRT;
using WinRT.Interop;

namespace AntigravityFrontend.Services;

public static class Win32WindowHelper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_SHOW = 5;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static void ActivatePopupSafely(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_SHOW);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);
        window.Activate();
    }

    public sealed class PopupDismissMonitor
    {
        private readonly Window _window;
        private readonly Func<bool> _shouldAutoClose;
        private readonly Action _dismissAction;
        private readonly DispatcherTimer _pollTimer;
        private long _armedAtTick;
        private bool _prevButtonDown;
        private IntPtr _initialForeground;

        public PopupDismissMonitor(Window window, Func<bool> shouldAutoClose, Action dismissAction)
        {
            _window = window;
            _shouldAutoClose = shouldAutoClose;
            _dismissAction = dismissAction;
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            _pollTimer.Tick += OnPollTick;
        }

        public void Start()
        {
            _armedAtTick = Environment.TickCount64 + 220;
            _prevButtonDown = IsMouseButtonDown();
            _initialForeground = GetForegroundWindow();
            if (!_pollTimer.IsEnabled)
                _pollTimer.Start();
        }

        public void Stop() => _pollTimer.Stop();

        private static bool IsMouseButtonDown()
            => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;

        private void OnPollTick(object? sender, object e)
        {
            if (!_shouldAutoClose())
            {
                _pollTimer.Stop();
                return;
            }

            if (Environment.TickCount64 < _armedAtTick)
            {
                _prevButtonDown = IsMouseButtonDown();
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(_window);
            if (hwnd == IntPtr.Zero) return;

            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != hwnd && _initialForeground == hwnd)
            {
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != (uint)Environment.ProcessId)
                {
                    Stop();
                    _dismissAction();
                    return;
                }
            }
            if (fg == hwnd)
                _initialForeground = hwnd;

            bool downNow = IsMouseButtonDown();
            bool pressedEdge = downNow && !_prevButtonDown;
            _prevButtonDown = downNow;
            if (!pressedEdge) return;

            if (!GetCursorPos(out POINT pt)) return;
            if (GetWindowRect(hwnd, out RECT rc) &&
                pt.X >= rc.Left && pt.X <= rc.Right && pt.Y >= rc.Top && pt.Y <= rc.Bottom)
            {
                return;
            }

            var hitHwnd = WindowFromPoint(pt);
            if (hitHwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hitHwnd, out uint hitPid);
                if (hitPid == (uint)Environment.ProcessId)
                    return;
            }

            Stop();
            _dismissAction();
        }
    }

    public static string GetForegroundHwndHex()
    {
        var fg = GetForegroundWindow();
        return $"0x{fg.ToInt64():x}";
    }

    public static bool IsAppWindowVisible(Window window)
    {
        try { return GetAppWindow(window).IsVisible; }
        catch { return false; }
    }

    public static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    public static double GetWindowScale(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    public static void ConfigureBorderlessPopup(Window window, bool hideFromAltTab = true, bool alwaysOnTop = true)
    {
        // Do not combine ExtendsContentIntoTitleBar = true with SetBorderAndTitleBar(false, false);
        // that combination causes a stray ~6px top black strip in WinUI 3.
        window.ExtendsContentIntoTitleBar = false;
        var hwnd = WindowNative.GetWindowHandle(window);
        var appWindow = GetAppWindow(window);
        try { appWindow.IsShownInSwitchers = !hideFromAltTab; } catch { }
        try
        {
            if (appWindow.TitleBar is { } titleBar)
                titleBar.ExtendsContentIntoTitleBar = false;
        }
        catch { }
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = alwaysOnTop;
            presenter.SetBorderAndTitleBar(false, false);
        }

        if (hideFromAltTab)
        {
            var ex = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
        }

        ApplyBorderlessDwmChrome(window);
    }

    public static LinearGradientBrush CreateGlassBorderBrush(bool isDark, byte topAlpha = 52, byte bottomAlpha = 14)
    {
        var grad = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(0, 1)
        };
        if (isDark)
        {
            grad.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb(topAlpha, 255, 255, 255), Offset = 0 });
            grad.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb(bottomAlpha, 255, 255, 255), Offset = 1 });
        }
        else
        {
            grad.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb(58, 255, 255, 255), Offset = 0 });
            grad.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb(26, 15, 23, 42), Offset = 1 });
        }
        return grad;
    }

    public static string GetHwndHex(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        return $"0x{hwnd.ToInt64():x}";
    }

    public static bool IsWindowVisible(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
    }

    public static void ConfigureTaskbarCapsuleWindow(Window window)
    {
        window.ExtendsContentIntoTitleBar = false;
        var hwnd = WindowNative.GetWindowHandle(window);
        var appWindow = GetAppWindow(window);
        try { appWindow.IsShownInSwitchers = false; } catch { }
        try
        {
            if (appWindow.TitleBar is { } titleBar)
                titleBar.ExtendsContentIntoTitleBar = false;
        }
        catch { }
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        var ex = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));

        ApplyBorderlessDwmChrome(window);
        HideWindowImmediate(window);
    }

    public static void PlaceTaskbarCapsule(Window window, int leftPx, int topPx, int widthPx, int heightPx, bool show)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero || widthPx <= 0 || heightPx <= 0)
            return;

        var appWindow = GetAppWindow(window);
        if (show)
        {
            var exBefore = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exBefore & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE))
                SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(exBefore | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));

            try
            {
                bool needsShow = !appWindow.IsVisible;
                appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(leftPx, topPx, widthPx, heightPx));
                if (needsShow)
                {
                    appWindow.Show(false);
                    appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(leftPx, topPx, widthPx, heightPx));
                }
            }
            catch { }

            var exAfter = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exAfter & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE))
                SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(exAfter | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));

            ApplyBorderlessDwmChrome(window);
            SetWindowPos(hwnd, HWND_TOPMOST, leftPx, topPx, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            var rgn = CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, heightPx, heightPx);
            if (rgn != IntPtr.Zero && SetWindowRgn(hwnd, rgn, true) == 0)
            {
                // OS only owns region when SetWindowRgn succeeds.
            }
            if (!IsWindowVisible(hwnd))
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        }
        else
        {
            try
            {
                appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(leftPx, topPx, widthPx, heightPx));
            }
            catch { }
            SetWindowPos(hwnd, HWND_TOPMOST, leftPx, topPx, widthPx, heightPx, SWP_NOACTIVATE | SWP_HIDEWINDOW);
        }
    }

    public static void HideWindowImmediate(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_HIDE);
        try { GetAppWindow(window).Hide(); } catch { }
    }

    public static void ApplyBorderlessDwmChrome(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);

        // Ensure Win32 caption/border flags left by WinUI 3 are stripped so no white frame/top line paints.
        ulong style = unchecked((ulong)GetWindowLongPtr64(hwnd, GWL_STYLE).ToInt64());
        ulong cleaned = style & ~(ulong)(WS_CAPTION | WS_BORDER | WS_THICKFRAME);
        if (cleaned != style)
            SetWindowLongPtr64(hwnd, GWL_STYLE, new IntPtr(unchecked((long)cleaned)));

        int cornerPref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref cornerPref, sizeof(int));
        int borderNone = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref borderNone, sizeof(int));

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static string DescribeWindowFrame(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        GetWindowRect(hwnd, out var wr);
        GetClientRect(hwnd, out var cr);
        long style = GetWindowLongPtr64(hwnd, GWL_STYLE).ToInt64();
        return $"style=0x{style:X8} win=({wr.Right - wr.Left}x{wr.Bottom - wr.Top}) client=({cr.Right - cr.Left}x{cr.Bottom - cr.Top})";
    }

    public static void SetAlwaysOnTop(Window window, bool onTop)
    {
        var appWindow = GetAppWindow(window);
        if (appWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = onTop;
    }

    public static (DesktopAcrylicController? Controller, SystemBackdropConfiguration? Config) AttachAcrylicBackdrop(
        Window window, bool isDark, double glassOpacity)
    {
        if (!DesktopAcrylicController.IsSupported())
            return (null, null);

        var config = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light
        };

        var controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };
        UpdateAcrylicTint(controller, isDark, glassOpacity);
        controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        controller.SetSystemBackdropConfiguration(config);

        window.Closed += (_, _) =>
        {
            controller.Dispose();
        };

        return (controller, config);
    }

    private sealed class AcrylicTintState
    {
        public bool IsDark;
        public float Opacity;
        public bool HasAccent;
        public uint AccentArgb;
    }

    private static readonly ConditionalWeakTable<DesktopAcrylicController, AcrylicTintState> AcrylicTintCache = new();

    public static void UpdateAcrylicTint(
        DesktopAcrylicController? controller,
        bool isDark,
        double glassOpacity,
        global::Windows.UI.Color? accentTint = null)
    {
        if (controller == null) return;
        var opacity = (float)Math.Clamp(glassOpacity, 0.35, 0.95);
        bool hasAccent = accentTint.HasValue;
        uint accentArgb = accentTint is { } ac
            ? ((uint)ac.A << 24) | ((uint)ac.R << 16) | ((uint)ac.G << 8) | ac.B
            : 0u;

        var cached = AcrylicTintCache.GetOrCreateValue(controller);
        if (cached.IsDark == isDark &&
            Math.Abs(cached.Opacity - opacity) < 0.001f &&
            cached.HasAccent == hasAccent &&
            cached.AccentArgb == accentArgb &&
            cached.Opacity > 0f)
        {
            return;
        }

        cached.IsDark = isDark;
        cached.Opacity = opacity;
        cached.HasAccent = hasAccent;
        cached.AccentArgb = accentArgb;

        if (isDark)
        {
            var baseCol = accentTint ?? global::Windows.UI.Color.FromArgb(255, 24, 28, 38);
            controller.TintColor = global::Windows.UI.Color.FromArgb(255,
                (byte)(18 + baseCol.R * 0.10),
                (byte)(22 + baseCol.G * 0.10),
                (byte)(30 + baseCol.B * 0.12));
            controller.FallbackColor = global::Windows.UI.Color.FromArgb(245, 22, 24, 32);
            controller.TintOpacity = opacity * 0.78f;
            controller.LuminosityOpacity = Math.Clamp(opacity * 0.85f, 0.45f, 0.92f);
        }
        else
        {
            var baseCol = accentTint ?? global::Windows.UI.Color.FromArgb(255, 242, 246, 252);
            controller.TintColor = global::Windows.UI.Color.FromArgb(255,
                (byte)Math.Min(255, 232 + baseCol.R * 0.08),
                (byte)Math.Min(255, 236 + baseCol.G * 0.08),
                (byte)Math.Min(255, 244 + baseCol.B * 0.08));
            controller.FallbackColor = global::Windows.UI.Color.FromArgb(245, 242, 246, 252);
            controller.TintOpacity = opacity * 0.65f;
            controller.LuminosityOpacity = Math.Clamp(opacity * 0.88f, 0.50f, 0.95f);
        }
    }

    public static RECT GetWorkAreaNearPoint(int physicalX, int physicalY)
    {
        var hMon = MonitorFromPoint(new POINT { X = physicalX, Y = physicalY }, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMon, ref mi))
            return mi.rcWork;
        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
    }

    public static void PositionPopupAboveOrBelowAnchor(
        Window popup,
        int anchorLeftPx,
        int anchorTopPx,
        int anchorRightPx,
        int anchorBottomPx,
        double widthDip,
        double heightDip,
        string direction)
    {
        var scale = GetWindowScale(popup);
        int widthPx = Math.Max(120, (int)Math.Round(widthDip * scale));
        int heightPx = Math.Max(120, (int)Math.Round(heightDip * scale));
        int marginPx = (int)Math.Round(10 * scale);

        int centerX = (anchorLeftPx + anchorRightPx) / 2;
        var work = GetWorkAreaNearPoint(centerX, anchorTopPx);
        int maxWorkHeight = Math.Max(220, (work.Bottom - work.Top) - marginPx * 2);
        if (heightPx > maxWorkHeight)
            heightPx = maxWorkHeight;
        int maxWorkWidth = Math.Max(220, (work.Right - work.Left) - marginPx * 2);
        if (widthPx > maxWorkWidth)
            widthPx = maxWorkWidth;

        int targetX = anchorRightPx - widthPx;
        if (targetX < work.Left + marginPx)
            targetX = work.Left + marginPx;
        if (targetX + widthPx > work.Right - marginPx)
            targetX = work.Right - marginPx - widthPx;

        bool openUp = !string.Equals(direction, "Down", StringComparison.OrdinalIgnoreCase);
        int targetY = openUp
            ? anchorTopPx - heightPx - marginPx
            : anchorBottomPx + marginPx;

        if (openUp && targetY < work.Top + marginPx)
            targetY = anchorBottomPx + marginPx;
        else if (!openUp && targetY + heightPx > work.Bottom - marginPx)
            targetY = anchorTopPx - heightPx - marginPx;

        targetY = Math.Clamp(targetY, work.Top + marginPx, Math.Max(work.Top + marginPx, work.Bottom - heightPx - marginPx));

        var appWindow = GetAppWindow(popup);
        appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(targetX, targetY, widthPx, heightPx));
    }

    public static bool IsSystemAppsDark()
    {
        try
        {
            var val = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                null);
            if (val is int i) return i == 0;
        }
        catch { }

        try
        {
            var bg = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
            return (bg.R + bg.G + bg.B) < 384;
        }
        catch
        {
            return true;
        }
    }

    public static bool IsSystemTaskbarDark()
    {
        try
        {
            var val = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme",
                null);
            if (val is int i) return i == 0;
        }
        catch { }

        return IsSystemAppsDark();
    }

    public static bool ResolveIsDark(AppearanceDto appearance, SettingsDto? settings = null, bool isTaskbar = false)
    {
        string pref = !string.IsNullOrWhiteSpace(appearance.Preference)
            ? appearance.Preference
            : (settings?.Theme ?? "System");

        if (string.Equals(pref, "Light", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(pref, "Dark", StringComparison.OrdinalIgnoreCase))
            return true;

        return isTaskbar ? IsSystemTaskbarDark() : IsSystemAppsDark();
    }

    public static void ApplySystemThemeIfFollowing(StateDto state)
    {
        string pref = !string.IsNullOrWhiteSpace(state.Appearance.Preference)
            ? state.Appearance.Preference
            : state.Settings.Theme;
        if (string.Equals(pref, "Light", StringComparison.OrdinalIgnoreCase))
        {
            state.Appearance.PopupTheme = "Light";
            state.Appearance.TaskbarTheme = "Light";
        }
        else if (string.Equals(pref, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            state.Appearance.PopupTheme = "Dark";
            state.Appearance.TaskbarTheme = "Dark";
        }
        else
        {
            state.Appearance.PopupTheme = IsSystemAppsDark() ? "Dark" : "Light";
            state.Appearance.TaskbarTheme = IsSystemTaskbarDark() ? "Dark" : "Light";
        }
    }

    public static void ApplyFontFamily(Control host, string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
            host.ClearValue(Control.FontFamilyProperty);
        else
            host.FontFamily = new FontFamily(fontFamily);
    }

    public static (int Left, int Top, int Right, int Bottom) GetWindowPhysicalRect(Window window)
    {
        var appWindow = GetAppWindow(window);
        var pos = appWindow.Position;
        var size = appWindow.Size;
        return (pos.X, pos.Y, pos.X + size.Width, pos.Y + size.Height);
    }

    public static UIElement CreateMistIcon(double size, bool isDark)
    {
        var baseCol = isDark
            ? global::Windows.UI.Color.FromArgb(255, 186, 204, 224)
            : global::Windows.UI.Color.FromArgb(255, 71, 85, 105);

        double barH = Math.Clamp(size * 0.145, 2.0, 5.0);
        double gap = Math.Clamp(size * 0.125, 1.5, 4.5);

        var stack = new StackPanel
        {
            Width = size,
            Spacing = gap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        (double WidthFrac, double OffsetXFrac, byte MaxAlpha)[] bars =
        [
            (0.76, 0.04, 185),
            (0.92, 0.00, 220),
            (0.68, 0.14, 155)
        ];

        foreach (var (wf, ox, alpha) in bars)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                EndPoint = new global::Windows.Foundation.Point(1, 0.5)
            };
            brush.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb((byte)(alpha * 0.35), baseCol.R, baseCol.G, baseCol.B), Offset = 0.0 });
            brush.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb(alpha, baseCol.R, baseCol.G, baseCol.B), Offset = 0.45 });
            brush.GradientStops.Add(new GradientStop { Color = global::Windows.UI.Color.FromArgb((byte)(alpha * 0.55), baseCol.R, baseCol.G, baseCol.B), Offset = 1.0 });

            stack.Children.Add(new Border
            {
                Width = size * wf,
                Height = barH,
                CornerRadius = new CornerRadius(barH / 2.0),
                Margin = new Thickness(size * ox, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = brush
            });
        }

        return stack;
    }
}
