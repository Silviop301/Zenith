using System.Globalization;
using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Interop;
using Zenith.Core.Resources;
using Zenith.Core.Settings;

namespace Zenith.Core.Presentation;

/// <summary>One journal entry as the Resgatados screen shows it.</summary>
public sealed record JournalLine(
    long Id,
    string Icon,
    string What,
    string From,
    string To,
    string When,
    JournalStatus Status,
    string StatusLabel,
    bool CanUndo,
    string? Note);

/// <summary>Turns journal entries into plain words: "Taxa do monitor: 59 Hz → 165 Hz" instead of keys and GUIDs.</summary>
public static class JournalPresenter
{
    /// <summary>
    /// What this user may see and undo: their own changes and the machine-wide ones, newest first.
    /// Changes another Windows user made stay out.
    /// </summary>
    public static IReadOnlyList<JournalLine> Lines(IEnumerable<JournalEntry> entries, string userSid) =>
        entries
            .Where(e => e.Scope == ChangeScope.System || e.UserSid == userSid)
            .OrderByDescending(e => e.Id)
            .Select(Line)
            .ToList();

    public static JournalLine Line(JournalEntry e) => new(
        e.Id,
        Icon(e.SettingKey),
        What(e),
        Value(e.SettingKey, e.PreviousValue),
        Value(e.SettingKey, e.NewValue),
        F(Strings.JournalWhen, e.AppliedAt.ToLocalTime()),
        e.Status,
        e.Status switch
        {
            JournalStatus.Applied => Strings.JournalStatusApplied,
            JournalStatus.Undone => Strings.JournalStatusUndone,
            JournalStatus.Superseded => Strings.JournalStatusSuperseded,
            _ => Strings.JournalStatusUndoFailed,
        },
        e.Status == JournalStatus.Applied,
        e.Note);

    private static string What(JournalEntry e) => e.SettingKey switch
    {
        RefreshRateSetting.SettingKey => Strings.JournalSettingRefresh,
        HdrSetting.SettingKey => Strings.JournalSettingHdr,
        ActivePowerSchemeSetting.SettingKey => Strings.JournalSettingScheme,
        ProcessorMaxStateAcSetting.SettingKey => Strings.JournalSettingMaxState,
        GpuPreferenceSetting.SettingKey => F(Strings.JournalSettingGpu, FileName(e.Target)),
        StartupSetting.UserKey or StartupSetting.MachineKey => F(Strings.JournalSettingStartup, StartupName(e.Target)),
        _ => e.SettingKey,
    };

    internal static string Value(string settingKey, string value) => settingKey switch
    {
        RefreshRateSetting.SettingKey => F(Strings.ValueHz, value),
        HdrSetting.SettingKey => value == HdrSetting.On ? Strings.ValueOn : Strings.ValueOff,
        ActivePowerSchemeSetting.SettingKey => SchemeName(value),
        ProcessorMaxStateAcSetting.SettingKey => F(Strings.ValuePercent, value),
        GpuPreferenceSetting.SettingKey => value switch
        {
            "0" => Strings.GpuWindowsDecides,
            "1" => Strings.GpuPowerSaving,
            GpuPreferenceSetting.HighPerformance => Strings.GpuHighPerformance,
            _ => Strings.GpuUnset,
        },
        StartupSetting.UserKey or StartupSetting.MachineKey => value switch
        {
            StartupSetting.Enabled => Strings.ValueStartupOn,
            StartupSetting.Disabled => Strings.ValueStartupOff,
            _ => Strings.ValueStartupAbsent,
        },
        _ => value,
    };

    /// <summary>The Run value name, or the shortcut's name without ".lnk".</summary>
    private static string StartupName(string target)
    {
        try
        {
            var name = StartupTarget.Parse(target).Name;
            return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
        catch (ArgumentException)
        {
            return target;
        }
    }

    private static string SchemeName(string value)
    {
        if (!Guid.TryParse(value, out var g)) return value;
        return g == PowerProfile.SchemePowerSaver ? Strings.SchemePowerSaver
            : g == PowerProfile.SchemeBalanced ? Strings.SchemeBalanced
            : g == PowerProfile.SchemeHighPerformance ? Strings.SchemeHighPerformance
            : g == PowerProfile.SchemeUltimatePerformance ? Strings.SchemeUltimatePerformance
            : Strings.SchemeCustom;
    }

    private static string Icon(string settingKey) => settingKey switch
    {
        RefreshRateSetting.SettingKey => "monitor",
        HdrSetting.SettingKey => "hdr",
        GpuPreferenceSetting.SettingKey => "gpu",
        StartupSetting.UserKey or StartupSetting.MachineKey => "startup",
        _ => "power",
    };

    // Targets are Windows paths; Path.GetFileName on Linux would not split on backslashes.
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
