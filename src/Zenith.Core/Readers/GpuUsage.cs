using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Zenith.Core.Readers;

/// <summary>Which GPU each process is using, and per-app GPU choices made in Windows Settings.</summary>
public static partial class GpuUsage
{
    public sealed record EngineInstance(int ProcessId, long AdapterLuid, string EngineType);

    public sealed record ProcessGpuUse(string ProcessName, long AdapterLuid, double Utilization3DPercent);

    /// <summary>Per-app choice in Settings > Display > Graphics: 0 = let Windows decide, 1 = power saving, 2 = high performance.</summary>
    public sealed record AppPreference(string ExecutableName, int GpuPreference);

    // "pid_1234_luid_0x00000000_0x0000C2D1_phys_0_eng_0_engtype_3D"
    [GeneratedRegex(@"^pid_(\d+)_luid_0x([0-9A-Fa-f]+)_0x([0-9A-Fa-f]+)_phys_\d+_eng_\d+_engtype_(.+)$")]
    private static partial Regex EngineInstanceRegex();

    [GeneratedRegex(@"(?:^|;)GpuPreference=(\d+);?")]
    private static partial Regex GpuPreferenceRegex();

    internal static EngineInstance? ParseEngineInstance(string name)
    {
        var m = EngineInstanceRegex().Match(name);
        if (!m.Success) return null;
        var high = Convert.ToInt64(m.Groups[2].Value, 16);
        var low = Convert.ToInt64(m.Groups[3].Value, 16);
        return new EngineInstance(int.Parse(m.Groups[1].Value), (high << 32) | low, m.Groups[4].Value);
    }

    internal static int? ParseGpuPreference(string value)
    {
        var m = GpuPreferenceRegex().Match(value);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    // Windows shell processes always draw on the GPU driving the desktop; they say nothing about games.
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "csrss", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "TextInputHost", "LockApp", "Widgets", "ApplicationFrameHost", "SystemSettings",
    };

    /// <summary>Like <see cref="Sample3DUsage"/>, with each executable's full path, for the fix. Not for reports: paths can contain the user name.</summary>
    public sealed record ProcessGpuPath(string ExecutablePath, long AdapterLuid);

    public static IReadOnlyList<ProcessGpuPath> Sample3DUsagePaths(TimeSpan window, double minPercent = 1.0) =>
        Sample(window, minPercent)
            .Select(u => ExecutablePath(u.Pid) is { } path ? new ProcessGpuPath(path, u.Luid) : null)
            .OfType<ProcessGpuPath>()
            .ToList();

    /// <summary>Samples the "GPU Engine" performance counters twice and returns processes doing 3D work.</summary>
    public static IReadOnlyList<ProcessGpuUse> Sample3DUsage(TimeSpan window, double minPercent = 1.0) =>
        Sample(window, minPercent)
            .Select(u => ProcessName(u.Pid) is { } name ? new ProcessGpuUse(name, u.Luid, Math.Round(u.Percent, 1)) : null)
            .OfType<ProcessGpuUse>()
            .OrderByDescending(p => p.Utilization3DPercent)
            .ToList();

    private static List<(int Pid, long Luid, double Percent)> Sample(TimeSpan window, double minPercent)
    {
        var category = new PerformanceCounterCategory("GPU Engine");
        var first = category.ReadCategory()["Utilization Percentage"];
        Thread.Sleep(window);
        var second = category.ReadCategory()["Utilization Percentage"];

        var usage = new Dictionary<(int Pid, long Luid), double>();
        foreach (InstanceData after in second.Values)
        {
            var instance = ParseEngineInstance(after.InstanceName);
            if (instance is null || !instance.EngineType.Equals("3D", StringComparison.OrdinalIgnoreCase)) continue;
            if (first[after.InstanceName] is not { } before) continue;

            var percent = CounterSample.Calculate(before.Sample, after.Sample);
            var key = (instance.ProcessId, instance.AdapterLuid);
            usage[key] = usage.GetValueOrDefault(key) + percent;
        }

        return usage
            .Where(kv => kv.Value >= minPercent && ProcessName(kv.Key.Pid) is { } name && !ShellProcesses.Contains(name))
            .Select(kv => (kv.Key.Pid, kv.Key.Luid, kv.Value))
            .ToList();
    }

    /// <summary>Per-app GPU choices. Only the executable name is kept; full paths can contain the user name.</summary>
    public static IReadOnlyList<AppPreference> ReadAppPreferences()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        if (key is null) return [];

        var result = new List<AppPreference>();
        foreach (var valueName in key.GetValueNames())
        {
            if (valueName.Equals("DirectXUserGlobalSettings", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.GetValue(valueName) is not string data || ParseGpuPreference(data) is not { } pref) continue;
            result.Add(new AppPreference(Path.GetFileName(valueName), pref));
        }
        return result;
    }

    private static string? ExecutablePath(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception)
        {
            return null; // exited between samples, or access denied
        }
    }
}
