using Microsoft.Win32;
using Zenith.Core.Changes;
using Zenith.Core.Readers;

namespace Zenith.Core.Settings;

/// <summary>
/// Per-app GPU choice from Settings > Display > Graphics, stored in the user's profile.
/// Target: full path of the executable. Value: "0" (Windows decides), "1" (power saving), "2" (high performance) or "unset".
/// </summary>
public sealed class GpuPreferenceSetting : ISetting
{
    public const string SettingKey = "gpu.app-preference";
    public const string Unset = "unset";
    public const string HighPerformance = "2";

    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public string Key => SettingKey;
    public ChangeScope Scope => ChangeScope.User;

    public string Read(string target)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(target) is string data && GpuUsage.ParseGpuPreference(data) is { } pref ? pref.ToString() : Unset;
    }

    public void Write(string target, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        var data = WithPreference(key.GetValue(target) as string, value);
        if (data is null) key.DeleteValue(target, throwOnMissingValue: false);
        else key.SetValue(target, data, RegistryValueKind.String);
    }

    /// <summary>Sets or removes only the GpuPreference field, keeping other per-app DirectX options. Null means remove the value.</summary>
    internal static string? WithPreference(string? existing, string value)
    {
        var parts = (existing ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (value != Unset) parts.Add($"GpuPreference={value}");
        return parts.Count == 0 ? null : string.Join(";", parts) + ";";
    }
}
