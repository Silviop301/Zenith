using Zenith.Core.Findings;

namespace Zenith.Core.Readers;

/// <summary>
/// Achado 4: a game runs on the integrated GPU while a discrete one is available.
/// Phase 0 has no list of known games yet, so it reports every non-shell process doing 3D work on the
/// integrated GPU, plus apps explicitly set to "power saving" in Windows Settings.
/// </summary>
public sealed class GameOnIntegratedGpuReader(TimeSpan? sampleWindow = null) : FindingReaderBase
{
    private readonly TimeSpan _sampleWindow = sampleWindow ?? TimeSpan.FromSeconds(1);

    public override string Id => FindingIds.GameOnIntegratedGpu;
    public override string Title => "Jogo rodando na placa de vídeo integrada";
    protected override string Source => "Contadores \"GPU Engine\" do Windows e preferências gráficas por app (UserGpuPreferences)";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["hasIntegratedGpu"] = machine.HasIntegratedGpu,
            ["hasDiscreteGpu"] = machine.HasDiscreteGpu,
        };

        if (!(machine.HasIntegratedGpu && machine.HasDiscreteGpu))
            return Result(FindingStatus.NotApplicable, "Este PC não tem duas placas de vídeo, então não há como um jogo usar a errada.", evidence);

        var integratedLuids = machine.GpuAdapters.Where(a => a.Kind == GpuKind.Integrated).Select(a => a.Luid).ToHashSet();
        var onIntegrated = GpuUsage.Sample3DUsage(_sampleWindow).Where(p => integratedLuids.Contains(p.AdapterLuid)).ToList();
        var powerSaving = GpuUsage.ReadAppPreferences().Where(p => p.GpuPreference == 1).ToList();

        evidence["processesOn3DIntegrated"] = onIntegrated;
        evidence["appsSetToPowerSaving"] = powerSaving.Select(p => p.ExecutableName).ToList();

        if (onIntegrated.Count == 0 && powerSaving.Count == 0)
            return Result(FindingStatus.NotDetected, "Nenhum programa aberto agora está usando a placa integrada para 3D.", evidence);

        var parts = new List<string>();
        if (onIntegrated.Count > 0)
            parts.Add("Usando a placa integrada agora: " + string.Join(", ", onIntegrated.Select(p => p.ProcessName)) + ".");
        if (powerSaving.Count > 0)
            parts.Add("Configurados para \"Economia de energia\": " + string.Join(", ", powerSaving.Select(p => p.ExecutableName)) + ".");
        return Result(FindingStatus.Detected, string.Join(" ", parts), evidence, new FindingChange("Integrada", "Dedicada"));
    }
}
