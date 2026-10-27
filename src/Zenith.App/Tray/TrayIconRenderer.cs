using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Zenith.Core.Presentation;

namespace Zenith.App.Tray;

/// <summary>
/// Draws the Ascensão tray icon (design/marca/zenith-bandeja-16*.svg) at the size Windows asks for: three dots
/// rising, the top one carrying the status. Dark dots on a light taskbar, light dots on a dark one.
/// </summary>
public static partial class TrayIconRenderer
{
    private static readonly Color Night = Color.FromArgb(0x17, 0x20, 0x36);
    private static readonly Color Light = Color.FromArgb(0xF4, 0xF1, 0xEA);
    private static readonly Color Active = Color.FromArgb(0x6B, 0x5B, 0x8E);
    private static readonly Color Alert = Color.FromArgb(0xB0, 0x7F, 0x2E);

    /// <summary>Windows 10 and 11 taskbars are dark unless the user picked the light Windows mode.</summary>
    public static bool LightTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    public static Bitmap Draw(TrayStatus status, bool lightTaskbar, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        var scale = size / 16f;
        var dots = lightTaskbar ? Night : Light;
        var (top, topRadius) = status switch
        {
            TrayStatus.Active => (Active, 2.68f),
            TrayStatus.Alert => (Alert, 2.68f),
            _ => (dots, 2.50f),
        };

        Dot(g, 3.38f, 11.25f, 1.25f, dots, scale);
        Dot(g, 6.62f, 8.00f, 1.75f, dots, scale);
        Dot(g, 11.38f, 6.00f, topRadius, top, scale);
        return bitmap;
    }

    /// <summary>An icon the caller owns: dispose it and then call <see cref="Destroy"/> with the same icon.</summary>
    public static Icon CreateIcon(TrayStatus status, bool lightTaskbar, int size)
    {
        using var bitmap = Draw(status, lightTaskbar, size);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>Icon.FromHandle does not free the handle that GetHicon made.</summary>
    public static void Destroy(Icon icon)
    {
        var handle = icon.Handle;
        icon.Dispose();
        DestroyIcon(handle);
    }

    private static void Dot(Graphics g, float cx, float cy, float r, Color color, float scale)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, (cx - r) * scale, (cy - r) * scale, 2 * r * scale, 2 * r * scale);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
