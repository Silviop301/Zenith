using Zenith.Core.Findings;

namespace Zenith.Core.Readers;

/// <summary>Achado 5: on a desktop with a discrete GPU, a monitor is plugged into the motherboard video output.</summary>
public sealed class MonitorOnMotherboardReader : FindingReaderBase
{
    public override string Id => FindingIds.MonitorOnMotherboard;
    public override string Title => "Monitor ligado no vídeo da placa-mãe";
    protected override string Source => "Lista de adaptadores de vídeo e saídas (DXGI) e tipo de gabinete (WMI)";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["chassis"] = machine.Chassis.ToString(),
            ["adapters"] = machine.GpuAdapters.Select(a => new { a.Description, kind = a.Kind.ToString(), outputs = a.AttachedOutputs }).ToList(),
        };

        // On laptops the built-in screen is wired to the integrated GPU by design.
        if (machine.Chassis == ChassisKind.Laptop)
            return Result(FindingStatus.NotApplicable, "Em notebooks a tela interna usa a placa integrada de propósito.", evidence);
        if (!machine.HasDiscreteGpu)
            return Result(FindingStatus.NotApplicable, "Este PC não tem placa de vídeo dedicada.", evidence);
        if (!machine.HasIntegratedGpu)
            return Result(FindingStatus.NotApplicable,
                "O processador deste PC não tem vídeo integrado, então as saídas da placa-mãe não funcionam.", evidence);

        var wrong = machine.GpuAdapters.Where(a => a.Kind == GpuKind.Integrated).SelectMany(a => a.AttachedOutputs).ToList();
        if (wrong.Count == 0)
            return Result(FindingStatus.NotDetected, "Todos os monitores estão ligados na placa de vídeo dedicada.", evidence);

        var names = machine.Displays.Where(d => wrong.Contains(d.GdiDeviceName)).Select(d => d.MonitorName ?? d.GdiDeviceName).ToList();
        var who = names.Count > 0 ? string.Join(", ", names) : $"{wrong.Count} monitor(es)";
        var caveat = machine.Chassis == ChassisKind.Unknown ? " (não deu para confirmar que é um PC de mesa)" : "";
        return Result(FindingStatus.Detected,
            $"{who} está ligado na saída de vídeo da placa-mãe, e não na placa de vídeo dedicada{caveat}.", evidence,
            new FindingChange("Placa-mãe", "Placa de vídeo"));
    }
}
