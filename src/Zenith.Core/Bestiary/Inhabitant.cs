namespace Zenith.Core.Bestiary;

public enum SignatureStatus
{
    /// <summary>Signed, and Windows trusts the signature (embedded or through a Windows catalog).</summary>
    Valid,
    Unsigned,
    /// <summary>Signed, but the file was changed after signing or the certificate is not trusted.</summary>
    Invalid,
    /// <summary>Could not be checked (no access to the file, or it is gone).</summary>
    Unknown,
}

/// <param name="Signer">Who signed it, as the certificate names them ("Microsoft Windows", "Valve Corp.").</param>
public sealed record SignatureInfo(SignatureStatus Status, string? Signer = null)
{
    public static readonly SignatureInfo NotChecked = new(SignatureStatus.Unknown);

    private static readonly IReadOnlySet<string> MicrosoftSigners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Windows", "Microsoft Corporation", "Microsoft Windows Publisher", "Microsoft Windows Hardware Compatibility Publisher",
    };

    public bool IsValid => Status == SignatureStatus.Valid;

    public bool IsMicrosoft => IsValid && Signer is not null && MicrosoftSigners.Contains(Signer);
}

/// <summary>Where Windows keeps a program that starts with it.</summary>
public enum StartupLocation
{
    /// <summary>HKCU\...\Run: this user only.</summary>
    UserRun,
    /// <summary>HKLM\...\Run: every user.</summary>
    MachineRun,
    /// <summary>HKLM\...\WOW6432Node\...\Run: every user, 32-bit programs.</summary>
    MachineRun32,
    /// <summary>The user's Startup folder.</summary>
    UserFolder,
    /// <summary>The Startup folder shared by every user.</summary>
    MachineFolder,
}

/// <summary>
/// One program that starts with Windows. <see cref="Name"/> is the Run value name or the file in the Startup folder.
/// Enabled is what Task Manager shows in its Startup tab.
/// </summary>
public sealed record StartupEntry(StartupLocation Location, string Name, string Command, ParsedCommand? Parsed, bool Enabled)
{
    public bool IsMachineWide => StartupTarget.IsMachineWide(Location);

    /// <summary>The journal target, e.g. "user-run|Discord".</summary>
    public string Target => StartupTarget.Format(Location, Name);
}

/// <summary>
/// One program in the PC: its running processes (grouped by file) and its startup entries.
/// Costs are null when Windows did not let Zenith read them.
/// </summary>
public sealed record Inhabitant(
    string ExecutablePath,
    string FileName,
    string? Description,
    string? Company,
    SignatureInfo Signature,
    IReadOnlyList<int> ProcessIds,
    long? MemoryBytes,
    double? CpuPercent,
    int? RemoteHosts,
    IReadOnlyList<StartupEntry> Startup)
{
    public bool IsRunning => ProcessIds.Count > 0;

    public bool StartsWithWindows => Startup.Any(s => s.Enabled);
}
