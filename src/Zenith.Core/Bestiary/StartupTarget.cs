namespace Zenith.Core.Bestiary;

/// <summary>
/// Journal targets for startup entries ("user-run|Discord", "machine-folder|App.lnk") and the
/// StartupApproved flag Windows uses for the switch in Task Manager's Startup tab.
/// </summary>
public static class StartupTarget
{
    private static readonly Dictionary<StartupLocation, string> Prefixes = new()
    {
        [StartupLocation.UserRun] = "user-run",
        [StartupLocation.MachineRun] = "machine-run",
        [StartupLocation.MachineRun32] = "machine-run32",
        [StartupLocation.UserFolder] = "user-folder",
        [StartupLocation.MachineFolder] = "machine-folder",
    };

    public static string Format(StartupLocation location, string name) => $"{Prefixes[location]}|{name}";

    public static (StartupLocation Location, string Name) Parse(string target)
    {
        var bar = target.IndexOf('|');
        if (bar > 0 && bar < target.Length - 1)
        {
            var prefix = target[..bar];
            foreach (var (location, p) in Prefixes)
                if (p == prefix) return (location, target[(bar + 1)..]);
        }
        throw new ArgumentException($"Alvo de inicialização inválido: {target}.");
    }

    public static bool IsMachineWide(StartupLocation location) =>
        location is StartupLocation.MachineRun or StartupLocation.MachineRun32 or StartupLocation.MachineFolder;

    /// <summary>The StartupApproved subkey (under HKCU or HKLM) that holds the switch for this location.</summary>
    public static string ApprovedSubkey(StartupLocation location) =>
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + location switch
        {
            StartupLocation.MachineRun32 => "Run32",
            StartupLocation.UserFolder or StartupLocation.MachineFolder => "StartupFolder",
            _ => "Run",
        };

    /// <summary>
    /// Windows reads the first byte: even (2, 6) is on, odd (3, 7) is off. No value means on.
    /// </summary>
    public static bool IsApproved(byte[]? flag) => flag is not { Length: > 0 } || (flag[0] & 1) == 0;

    /// <summary>The 12-byte value Task Manager writes: the flag, three zero bytes, then when it was turned off.</summary>
    public static byte[] ApprovedFlag(bool enabled, DateTime nowUtc)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }
}
