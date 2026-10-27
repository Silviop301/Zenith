using Zenith.Core.Findings;
using Zenith.Core.Interop;

namespace Zenith.Core.Readers;

/// <summary>Achado 3: the active power plan caps the processor.</summary>
public sealed class PowerPlanReader : FindingReaderBase
{
    public override string Id => FindingIds.PowerPlan;
    public override string Title => "Plano de energia limitando o processador";
    protected override string Source => "API de energia do Windows (powrprof)";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        var scheme = PowerProfile.GetActiveScheme();
        var name = PowerProfile.GetFriendlyName(scheme);
        var maxAc = PowerProfile.ReadAc(scheme, PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMaxState);
        var maxDc = PowerProfile.ReadDc(scheme, PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMaxState);
        var minAc = PowerProfile.ReadAc(scheme, PowerProfile.SubgroupProcessor, PowerProfile.ProcessorMinState);
        var overlay = PowerProfile.GetActualOverlay();

        var evidence = new Dictionary<string, object?>
        {
            ["schemeGuid"] = scheme,
            ["schemeName"] = name,
            ["schemeKind"] = SchemeKind(scheme),
            ["processorMaxStateAcPercent"] = maxAc,
            ["processorMaxStateDcPercent"] = maxDc,
            ["processorMinStateAcPercent"] = minAc,
            ["powerModeOverlay"] = overlay is { } o ? OverlayKind(o) : null,
            ["chassis"] = machine.Chassis.ToString(),
        };

        var reasons = new List<string>();
        if (scheme == PowerProfile.SchemePowerSaver)
            reasons.Add($"o plano ativo é \"{name ?? "Economia de energia"}\"");
        if (maxAc is < 100)
            reasons.Add($"o processador está limitado a {maxAc}% na tomada");
        if (overlay == PowerProfile.OverlayBestEfficiency)
            reasons.Add("o modo de energia está em \"Melhor eficiência energética\"");

        if (reasons.Count == 0)
            return Result(FindingStatus.NotDetected,
                $"O plano de energia ({name ?? "desconhecido"}) não limita o processador.", evidence);

        // Only what PowerPlanFix changes; the power mode overlay is left to the user (see the guide).
        FindingChange? change = scheme == PowerProfile.SchemePowerSaver ? new FindingChange("Economia de energia", "Equilibrado")
            : maxAc is < 100 ? new FindingChange($"{maxAc}%", "100%")
            : null;
        return Result(FindingStatus.Detected, "O processador não está rendendo tudo: " + string.Join("; ", reasons) + ".", evidence, change);
    }

    private static string SchemeKind(Guid g) =>
        g == PowerProfile.SchemePowerSaver ? "power-saver"
        : g == PowerProfile.SchemeBalanced ? "balanced"
        : g == PowerProfile.SchemeHighPerformance ? "high-performance"
        : g == PowerProfile.SchemeUltimatePerformance ? "ultimate-performance"
        : "custom";

    private static string OverlayKind(Guid g) =>
        g == PowerProfile.OverlayBestEfficiency ? "best-efficiency"
        : g == PowerProfile.OverlayBalanced ? "balanced"
        : g == PowerProfile.OverlayBetterPerformance ? "better-performance"
        : g == PowerProfile.OverlayBestPerformance ? "best-performance"
        : g.ToString();
}
