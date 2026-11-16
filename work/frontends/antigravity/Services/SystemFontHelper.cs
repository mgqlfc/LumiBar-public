using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AntigravityFrontend.Sdk;

namespace AntigravityFrontend.Services;

/// <summary>
/// Reads real installed Windows font families via GDI EnumFontFamiliesExW as a fallback when running in offline mock mode.
/// </summary>
public static class SystemFontHelper
{
    private const byte DEFAULT_CHARSET = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    private delegate int EnumFontExDelegate(ref LOGFONTW lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesExW(IntPtr hdc, ref LOGFONTW lpLogfont, EnumFontExDelegate lpProc, IntPtr lParam, uint dwFlags);

    private static List<SystemFontItemDto>? _cached;

    public static List<SystemFontItemDto> GetInstalledFonts(bool refresh = false)
    {
        if (!refresh && _cached != null)
            return _cached;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            try
            {
                var lf = new LOGFONTW { lfCharSet = DEFAULT_CHARSET, lfFaceName = string.Empty };
                EnumFontExDelegate cb = (ref LOGFONTW elf, IntPtr _, uint _, IntPtr _) =>
                {
                    var face = elf.lfFaceName;
                    if (!string.IsNullOrWhiteSpace(face) && !face.StartsWith("@", StringComparison.Ordinal))
                        names.Add(face);
                    return 1;
                };
                EnumFontFamiliesExW(hdc, ref lf, cb, IntPtr.Zero, 0);
                GC.KeepAlive(cb);
            }
            catch { }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        _cached = names
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Select(n => new SystemFontItemDto
            {
                Family = n,
                DisplayName = n,
                Aliases = new List<string> { n }
            })
            .ToList();
        return _cached;
    }
}
