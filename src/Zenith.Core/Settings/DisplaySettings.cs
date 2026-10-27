using System.Runtime.InteropServices;
using Zenith.Core.Changes;
using Zenith.Core.Interop;

namespace Zenith.Core.Settings;

/// <summary>Finds an active display by its monitor device path, which stays the same across reboots, unlike \\.\DISPLAYn.</summary>
internal static class DisplayTargets
{
    public sealed record Target(DisplayConfig.PathInfo Path, string GdiDeviceName);

    public static Target Find(string monitorDevicePath)
    {
        var (paths, _) = DisplayConfig.QueryActivePaths();
        foreach (var path in paths)
        {
            if (DisplayConfig.GetTargetName(path.TargetInfo) is not { } name) continue;
            if (!string.Equals(name.MonitorDevicePath, monitorDevicePath, StringComparison.OrdinalIgnoreCase)) continue;
            var gdi = DisplayConfig.GetGdiDeviceName(path.SourceInfo)
                ?? throw new InvalidOperationException("Não deu para identificar a saída de vídeo do monitor.");
            return new Target(path, gdi);
        }
        throw new InvalidOperationException("O monitor não está conectado ou ativo.");
    }
}

/// <summary>Refresh rate of one monitor at its current resolution. Target: monitor device path. Value: whole Hz.</summary>
public sealed class RefreshRateSetting : ISetting
{
    public const string SettingKey = "display.refresh-rate";

    public string Key => SettingKey;
    public ChangeScope Scope => ChangeScope.User;

    public string Read(string target) => Current(DisplayTargets.Find(target).GdiDeviceName).DisplayFrequency.ToString();

    public void Write(string target, string value)
    {
        var gdi = DisplayTargets.Find(target).GdiDeviceName;
        var mode = Current(gdi);
        mode.DisplayFrequency = uint.Parse(value);
        mode.Fields = DisplayConfig.DmPelsWidth | DisplayConfig.DmPelsHeight | DisplayConfig.DmDisplayFrequency;
        var result = DisplayConfig.ChangeDisplaySettingsEx(gdi, ref mode, IntPtr.Zero, DisplayConfig.CdsUpdateRegistry, IntPtr.Zero);
        if (result != DisplayConfig.DispChangeSuccessful)
            throw new InvalidOperationException($"O Windows recusou a taxa de {value} Hz (código {result}).");
    }

    private static DisplayConfig.DevMode Current(string gdiDeviceName)
    {
        var mode = DisplayConfig.NewDevMode();
        if (!DisplayConfig.EnumDisplaySettings(gdiDeviceName, DisplayConfig.EnumCurrentSettings, ref mode))
            throw new InvalidOperationException("Não deu para ler o modo atual do monitor.");
        return mode;
    }
}

/// <summary>HDR on or off for one monitor. Target: monitor device path. Value: "on" or "off".</summary>
public sealed class HdrSetting : ISetting
{
    public const string SettingKey = "display.hdr";
    public const string On = "on";
    public const string Off = "off";

    public string Key => SettingKey;
    public ChangeScope Scope => ChangeScope.User;

    public string Read(string target)
    {
        var info = DisplayTargets.Find(target).Path.TargetInfo;
        var state = MachineSnapshot.ReadAdvancedColor(info) ?? throw new InvalidOperationException("O Windows não informou o estado do HDR.");
        return state.HdrEnabled ? On : Off;
    }

    public void Write(string target, string value)
    {
        var info = DisplayTargets.Find(target).Path.TargetInfo;
        var enable = value == On ? 1u : 0u;

        // Windows 11 24H2 has a dedicated HDR switch; older builds use the advanced color switch.
        foreach (var type in new[] { DisplayConfig.DeviceInfoType.SetHdrState, DisplayConfig.DeviceInfoType.SetAdvancedColorState })
        {
            var request = new DisplayConfig.SetEnableState
            {
                Header = new DisplayConfig.DeviceInfoHeader
                {
                    Type = type,
                    Size = (uint)Marshal.SizeOf<DisplayConfig.SetEnableState>(),
                    AdapterId = info.AdapterId,
                    Id = info.Id,
                },
                Value = enable,
            };
            if (DisplayConfig.SetDeviceInfo(ref request) == DisplayConfig.ErrorSuccess)
            {
                WaitUntil(target, value);
                return;
            }
        }
        throw new InvalidOperationException("O Windows recusou a mudança do HDR.");
    }

    // The display pipeline takes a moment to switch modes; the engine verifies right after Write returns.
    private void WaitUntil(string target, string value)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && Read(target) != value) Thread.Sleep(200);
    }
}
