using Zenith.Core.Readers;

namespace Zenith.Core.Findings;

public sealed record ScanReport(DateTimeOffset ScannedAt, MachineSnapshot Machine, IReadOnlyList<FindingResult> Findings);

/// <summary>Runs every Resgate reader. Read-only: nothing on the PC is changed.</summary>
public static class Scanner
{
    public static IReadOnlyList<IFindingReader> DefaultReaders() =>
    [
        new DisplayRefreshRateReader(),
        new HdrReader(),
        new PowerPlanReader(),
        new GameOnIntegratedGpuReader(),
        new MonitorOnMotherboardReader(),
        new MemorySpeedReader(),
    ];

    public static ScanReport Run(IEnumerable<IFindingReader>? readers = null)
    {
        var machine = MachineSnapshot.Capture();
        var results = (readers ?? DefaultReaders()).Select(r => r.Read(machine)).ToList();
        return new ScanReport(DateTimeOffset.Now, machine, results);
    }
}
