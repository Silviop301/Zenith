using Zenith.Core.Changes;
using Zenith.Core.Interop;

namespace Zenith.Core.Settings;

/// <summary>The active power plan, machine-wide. Value: the plan's GUID.</summary>
public sealed class ActivePowerSchemeSetting : ISetting
{
    public const string SettingKey = "power.active-scheme";
    public const string MachineTarget = "machine";

    public string Key => SettingKey;
    public ChangeScope Scope => ChangeScope.System;

    public string Read(string target) => PowerProfile.GetActiveScheme().ToString("D");

    public void Write(string target, string value) => PowerProfile.SetActiveScheme(Guid.Parse(value));
}

/// <summary>"Maximum processor state" while plugged in, for one power plan. Target: the plan's GUID. Value: percent.</summary>
public sealed class ProcessorMaxStateAcSetting : ISetting
{
    public const string SettingKey = "power.processor-max-state-ac";

    public string Key => SettingKey;
    public ChangeScope Scope => ChangeScope.System;

    public string Read(string target) =>
        PowerProfile.ReadAc(Guid.Parse(target), PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMaxState)?.ToString()
        ?? throw new InvalidOperationException("Não deu para ler o estado máximo do processador.");

    public void Write(string target, string value) =>
        PowerProfile.WriteAc(Guid.Parse(target), PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMaxState, uint.Parse(value));
}
