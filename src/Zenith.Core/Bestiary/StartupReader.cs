using Microsoft.Win32;
using Zenith.Core.Interop;

namespace Zenith.Core.Bestiary;

/// <summary>
/// Programs that start with Windows, from the same places Task Manager's Startup tab reads:
/// the Run keys and the Startup folders, each with its on/off switch in StartupApproved.
/// Scheduled tasks and services are not listed yet.
/// </summary>
public static class StartupReader
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Every entry, or only the machine-wide ones (what the service may change).</summary>
    public static IReadOnlyList<StartupEntry> Read(bool machineOnly = false)
    {
        var entries = new List<StartupEntry>();
        if (!machineOnly)
        {
            entries.AddRange(FromRunKey(StartupLocation.UserRun));
            entries.AddRange(FromFolder(StartupLocation.UserFolder));
        }
        entries.AddRange(FromRunKey(StartupLocation.MachineRun));
        entries.AddRange(FromRunKey(StartupLocation.MachineRun32));
        entries.AddRange(FromFolder(StartupLocation.MachineFolder));
        return entries;
    }

    /// <summary>One entry by location and name, or null when it is not there.</summary>
    public static StartupEntry? Find(StartupLocation location, string name)
    {
        if (location is StartupLocation.UserFolder or StartupLocation.MachineFolder)
        {
            var path = Path.Combine(FolderPath(location), name);
            // Only a file directly in the folder: never a path that climbs out of it.
            return name.IndexOfAny(['\\', '/']) < 0 && File.Exists(path) ? FolderEntry(location, path) : null;
        }
        using var key = RunRoot(location).OpenSubKey(RunKeyPath(location));
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command
            ? new StartupEntry(location, name, command, CommandLine.Parse(command), IsEnabled(location, name))
            : null;
    }

    public static RegistryKey Root(StartupLocation location) =>
        StartupTarget.IsMachineWide(location) ? Registry.LocalMachine : Registry.CurrentUser;

    public static bool IsEnabled(StartupLocation location, string name)
    {
        using var key = Root(location).OpenSubKey(StartupTarget.ApprovedSubkey(location));
        return StartupTarget.IsApproved(key?.GetValue(name) as byte[]);
    }

    public static void SetEnabled(StartupLocation location, string name, bool enabled)
    {
        using var key = Root(location).CreateSubKey(StartupTarget.ApprovedSubkey(location));
        key.SetValue(name, StartupTarget.ApprovedFlag(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
    }

    private static IEnumerable<StartupEntry> FromRunKey(StartupLocation location)
    {
        using var key = RunRoot(location).OpenSubKey(RunKeyPath(location));
        if (key is null) yield break;
        foreach (var name in key.GetValueNames())
        {
            // The unnamed default value is not a startup entry.
            if (name.Length == 0 || key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command) continue;
            yield return new StartupEntry(location, name, command, CommandLine.Parse(command), IsEnabled(location, name));
        }
    }

    private static IEnumerable<StartupEntry> FromFolder(StartupLocation location)
    {
        var folder = FolderPath(location);
        if (!Directory.Exists(folder)) yield break;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            yield return FolderEntry(location, path);
        }
    }

    private static StartupEntry FolderEntry(StartupLocation location, string path)
    {
        var name = Path.GetFileName(path);
        var command = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ShellLink.CommandLine(path) ?? ""
            : $"\"{path}\"";
        return new StartupEntry(location, name, command, CommandLine.Parse(command), IsEnabled(location, name));
    }

    private static RegistryKey RunRoot(StartupLocation location) =>
        location == StartupLocation.UserRun ? Registry.CurrentUser : Registry.LocalMachine;

    private static string RunKeyPath(StartupLocation location) => location == StartupLocation.MachineRun32 ? Run32Key : RunKey;

    public static string FolderPath(StartupLocation location) => Environment.GetFolderPath(
        location == StartupLocation.MachineFolder ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
}
