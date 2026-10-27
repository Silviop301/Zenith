using Zenith.Core.Bestiary;
using Zenith.Core.Changes;

namespace Zenith.Core.Settings;

/// <summary>
/// The on/off switch of one startup entry, the same one Task Manager's Startup tab shows. Turning it off keeps the
/// entry, so the program is untouched and turning it back on restores it exactly. Target: "user-run|Name" and the like.
/// Value: "enabled", "disabled" or "absent" (the entry no longer exists; it cannot be written).
/// Two instances: this user's entries (applied by the agent) and the machine-wide ones (applied by the service).
/// </summary>
public sealed class StartupSetting(bool machineWide) : ISetting
{
    public const string UserKey = "startup.user";
    public const string MachineKey = "startup.machine";

    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
    public const string Absent = "absent";

    public string Key => machineWide ? MachineKey : UserKey;
    public ChangeScope Scope => machineWide ? ChangeScope.System : ChangeScope.User;

    public static string KeyFor(StartupLocation location) => StartupTarget.IsMachineWide(location) ? MachineKey : UserKey;

    public string Read(string target)
    {
        var (location, name) = Parse(target);
        var entry = StartupReader.Find(location, name);
        return entry is null ? Absent : entry.Enabled ? Enabled : Disabled;
    }

    public void Write(string target, string value)
    {
        var (location, name) = Parse(target);
        if (value is not (Enabled or Disabled)) throw new ArgumentException($"Valor inválido para a inicialização: {value}.");
        if (StartupReader.Find(location, name) is null)
            throw new InvalidOperationException("Este programa não está mais na inicialização do Windows.");
        StartupReader.SetEnabled(location, name, value == Enabled);
    }

    private (StartupLocation Location, string Name) Parse(string target)
    {
        var parsed = StartupTarget.Parse(target);
        if (StartupTarget.IsMachineWide(parsed.Location) != machineWide)
            throw new ArgumentException($"O alvo {target} não é {(machineWide ? "do computador" : "do usuário")}.");
        return parsed;
    }
}
