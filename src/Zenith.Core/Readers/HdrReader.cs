using Zenith.Core.Findings;

namespace Zenith.Core.Readers;

/// <summary>Achado 2: a monitor supports HDR but it is turned off.</summary>
public sealed class HdrReader : FindingReaderBase
{
    public override string Id => FindingIds.Hdr;
    public override string Title => "HDR disponível e desligado";
    protected override string Source => "Informações de cor avançada da API de tela do Windows (DisplayConfigGetDeviceInfo)";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["displays"] = machine.Displays.Select(d => new
            {
                monitor = d.MonitorName,
                api = d.AdvancedColor?.ApiVersion,
                hdrSupported = d.AdvancedColor?.HdrSupported,
                hdrEnabled = d.AdvancedColor?.HdrEnabled,
                limitedByPolicy = d.AdvancedColor?.LimitedByPolicy,
                bitsPerColorChannel = d.AdvancedColor?.BitsPerColorChannel,
            }).ToList(),
        };

        var known = machine.Displays.Where(d => d.AdvancedColor is not null).ToList();
        if (known.Count == 0)
            return Result(FindingStatus.Inconclusive, "O Windows não informou se os monitores têm HDR.", evidence);

        var supported = known.Where(d => d.AdvancedColor!.HdrSupported).ToList();
        if (supported.Count == 0)
            return Result(FindingStatus.NotApplicable, "Nenhum monitor conectado tem suporte a HDR.", evidence);

        var off = supported.Where(d => !d.AdvancedColor!.HdrEnabled).ToList();
        if (off.Count == 0)
            return Result(FindingStatus.NotDetected, "O HDR já está ligado.", evidence);

        var summary = string.Join(" ", off.Select(d =>
            $"{d.MonitorName ?? "Seu monitor"} tem HDR, mas ele está desligado" +
            (d.AdvancedColor!.LimitedByPolicy ? " (bloqueado por uma política do sistema)." : ".")));
        return Result(FindingStatus.Detected, summary, evidence, new FindingChange("Desligado", "Ligado"));
    }
}
