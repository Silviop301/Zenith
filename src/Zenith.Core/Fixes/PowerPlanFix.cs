using Zenith.Core.Changes;
using Zenith.Core.Interop;
using Zenith.Core.Findings;
using Zenith.Core.Readers;
using Zenith.Core.Settings;

namespace Zenith.Core.Fixes;

/// <summary>
/// Achado 3: leaves "Economia de energia" for "Equilibrado" and lifts a processor cap below 100%.
/// Balanced is the target, not High performance: on current CPUs it reaches full boost under load and
/// still idles down, so nothing is lost and no promise of extra speed is made.
/// </summary>
public static class PowerPlanFix
{
    public static readonly Guid TargetScheme = PowerProfile.SchemeBalanced;

    internal static bool NeedsSchemeChange(Guid activeScheme) => activeScheme == PowerProfile.SchemePowerSaver;

    internal static bool NeedsMaxStateChange(uint? maxStateAc) => maxStateAc is < 100;

    public static IReadOnlyList<JournalEntry> Apply(ChangeEngine engine)
    {
        var applied = new List<JournalEntry>();
        var findingId = FindingIds.PowerPlan;

        if (NeedsSchemeChange(PowerProfile.GetActiveScheme()) &&
            engine.Apply(findingId, ActivePowerSchemeSetting.SettingKey, ActivePowerSchemeSetting.MachineTarget,
                TargetScheme.ToString("D")) is { } schemeChange)
            applied.Add(schemeChange);

        // Checked after the plan switch, since the new plan can carry its own cap.
        var active = PowerProfile.GetActiveScheme();
        if (NeedsMaxStateChange(PowerProfile.ReadAc(active, PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMaxState)) &&
            engine.Apply(findingId, ProcessorMaxStateAcSetting.SettingKey, active.ToString("D"), "100") is { } capChange)
            applied.Add(capChange);

        return applied;
    }
}
