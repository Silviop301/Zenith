using Zenith.Core.Findings;

namespace Zenith.Core.Readers;

/// <summary>Achado 1: monitor running below the highest refresh rate it supports at the current resolution.</summary>
public sealed class DisplayRefreshRateReader : FindingReaderBase
{
    // 164.99 Hz and 165 Hz are the same mode; anything closer than this is not a finding.
    private const double ToleranceHz = 1.0;

    public override string Id => FindingIds.RefreshRate;
    public override string Title => "Monitor abaixo da taxa máxima";
    protected override string Source => "API de configuração de tela do Windows (QueryDisplayConfig, EnumDisplaySettings)";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        if (machine.Displays.Count == 0)
            return Result(FindingStatus.Inconclusive, "Nenhum monitor ativo foi encontrado.", []);

        var below = machine.Displays
            .Where(d => d.MaxRefreshHzAtCurrentResolution > 0 && d.MaxRefreshHzAtCurrentResolution - d.CurrentRefreshHz > ToleranceHz)
            .ToList();

        var evidence = new Dictionary<string, object?>
        {
            ["displays"] = machine.Displays.Select(d => new
            {
                monitor = d.MonitorName,
                resolution = $"{d.Width}x{d.Height}",
                currentHz = d.CurrentRefreshHz,
                maxHzAtThisResolution = d.MaxRefreshHzAtCurrentResolution,
                supportedHz = d.RefreshRatesAtCurrentResolution,
            }).ToList(),
        };

        if (below.Count == 0)
        {
            var d = machine.Displays[0];
            return Result(FindingStatus.NotDetected,
                machine.Displays.Count == 1
                    ? $"Seu monitor já está na taxa máxima ({Hz(d.CurrentRefreshHz)} Hz)."
                    : "Todos os monitores já estão na taxa máxima.",
                evidence);
        }

        var summary = string.Join(" ", below.Select(d =>
            $"{d.MonitorName ?? "Seu monitor"} está em {Hz(d.CurrentRefreshHz)} Hz, mas aceita até {d.MaxRefreshHzAtCurrentResolution} Hz nesta resolução."));
        var first = below[0];
        return Result(FindingStatus.Detected, summary, evidence,
            new FindingChange($"{Hz(first.CurrentRefreshHz)} Hz", $"{first.MaxRefreshHzAtCurrentResolution} Hz"));
    }

    private static string Hz(double value) => Math.Round(value).ToString("0");
}
