using Microsoft.Win32;
using Zenith.Core.Changes;
using Zenith.Core.Readers;
using Zenith.Core.Settings;

namespace Zenith.Core.Fixes;

/// <summary>Fixes applied by the user-session agent: they change the user's display and profile, which the service cannot reach.</summary>
public static class UserFixes
{
    /// <summary>Achado 1: each monitor to its highest refresh rate at the current resolution.</summary>
    public static IReadOnlyList<JournalEntry> RefreshRate(ChangeEngine engine, MachineSnapshot machine, string userSid)
    {
        var findingId = new DisplayRefreshRateReader().Id;
        var applied = new List<JournalEntry>();
        foreach (var display in machine.Displays)
        {
            if (display.MonitorDevicePath is null || display.MaxRefreshHzAtCurrentResolution - display.CurrentRefreshHz <= 1.0) continue;
            if (engine.Apply(findingId, RefreshRateSetting.SettingKey, display.MonitorDevicePath,
                    display.MaxRefreshHzAtCurrentResolution.ToString(), userSid) is { } entry)
                applied.Add(entry);
        }
        return applied;
    }

    /// <summary>Achado 2: turns HDR on for monitors that support it. The caller runs the 15-second keep-or-revert test.</summary>
    public static IReadOnlyList<JournalEntry> Hdr(ChangeEngine engine, MachineSnapshot machine, string userSid)
    {
        var findingId = new HdrReader().Id;
        var applied = new List<JournalEntry>();
        foreach (var display in machine.Displays)
        {
            if (display.MonitorDevicePath is null || display.AdvancedColor is not { HdrSupported: true, HdrEnabled: false }) continue;
            if (engine.Apply(findingId, HdrSetting.SettingKey, display.MonitorDevicePath, HdrSetting.On, userSid) is { } entry)
                applied.Add(entry);
        }
        return applied;
    }

    /// <summary>
    /// Achado 4: "high performance" for apps now drawing 3D on the integrated GPU and apps set to "power saving".
    /// Only on PCs with both kinds of GPU.
    /// </summary>
    public static IReadOnlyList<JournalEntry> GpuPreference(ChangeEngine engine, MachineSnapshot machine, string userSid)
    {
        if (!(machine.HasIntegratedGpu && machine.HasDiscreteGpu)) return [];

        var integratedLuids = machine.GpuAdapters.Where(a => a.Kind == GpuKind.Integrated).Select(a => a.Luid).ToHashSet();
        var paths = GpuUsage.Sample3DUsagePaths(TimeSpan.FromSeconds(1))
            .Where(p => integratedLuids.Contains(p.AdapterLuid))
            .Select(p => p.ExecutablePath)
            .Concat(PowerSavingApps())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var findingId = new GameOnIntegratedGpuReader().Id;
        var applied = new List<JournalEntry>();
        foreach (var path in paths)
        {
            if (engine.Apply(findingId, GpuPreferenceSetting.SettingKey, path, GpuPreferenceSetting.HighPerformance, userSid) is { } entry)
                applied.Add(entry);
        }
        return applied;
    }

    private static IEnumerable<string> PowerSavingApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        if (key is null) yield break;
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string data && GpuUsage.ParseGpuPreference(data) == 1 && File.Exists(name))
                yield return name;
        }
    }
}
