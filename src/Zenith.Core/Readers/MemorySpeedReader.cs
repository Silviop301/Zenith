using Zenith.Core.Findings;

namespace Zenith.Core.Readers;

/// <summary>Achado 6: memory running below its rated (XMP/EXPO) speed. The fix is a BIOS guide, never automatic.</summary>
public sealed class MemorySpeedReader : FindingReaderBase
{
    // Firmware often reports 3600 as 3598 or similar.
    private const uint ToleranceMTs = 50;

    public override string Id => FindingIds.MemorySpeed;
    public override string Title => "Memória abaixo da velocidade de fábrica";
    protected override string Source => "WMI (Win32_PhysicalMemory) e base de modelos de memória do Zenith";

    protected override FindingResult ReadCore(MachineSnapshot machine)
    {
        if (machine.MemoryModules.Count == 0)
            return Result(FindingStatus.Inconclusive, "O Windows não informou os pentes de memória.", []);

        var modules = machine.MemoryModules.Select(m => new
        {
            m.Manufacturer,
            m.PartNumber,
            m.CapacityMiB,
            wmiSpeed = m.SpeedMHz,
            configuredMTs = m.ConfiguredClockSpeedMHz,
            rated = MemoryRating.Lookup(m.PartNumber),
        }).ToList();

        var evidence = new Dictionary<string, object?>
        {
            ["modules"] = modules.Select(m => new
            {
                manufacturer = m.Manufacturer,
                partNumber = m.PartNumber,
                capacityMiB = m.CapacityMiB,
                wmiSpeed = m.wmiSpeed,
                configuredMTs = m.configuredMTs,
                ratedMTs = m.rated.MTs,
                ratedSource = m.rated.Source.ToString(),
            }).ToList(),
        };

        var comparable = modules.Where(m => m.rated.MTs is not null && m.configuredMTs is not null).ToList();
        if (comparable.Count == 0)
            return Result(FindingStatus.Inconclusive,
                "Não deu para saber a velocidade de fábrica destes pentes. O modelo ainda não está na base do Zenith.", evidence);

        var slow = comparable.Where(m => m.configuredMTs! + ToleranceMTs < m.rated.MTs!).ToList();
        if (slow.Count == 0)
        {
            var m = comparable[0];
            return Result(FindingStatus.NotDetected, $"A memória já está na velocidade de fábrica ({m.configuredMTs} MHz).", evidence);
        }

        var s = slow[0];
        var estimate = s.rated.Source == RatedSpeedSource.PartNumber ? " (velocidade de fábrica estimada pelo código da peça)" : "";
        return Result(FindingStatus.Detected,
            $"A memória está rodando a {s.configuredMTs} MHz, mas foi feita para {s.rated.MTs} MHz{estimate}. " +
            "Dá para ativar o perfil XMP/EXPO na BIOS.", evidence,
            new FindingChange($"{s.configuredMTs} MHz", $"{s.rated.MTs} MHz"));
    }
}
