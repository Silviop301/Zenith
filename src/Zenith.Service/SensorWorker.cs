using Zenith.Core.Sensors;

namespace Zenith.Service;

/// <summary>Silent sensor collection: one pass a minute, each source at its own interval (see <see cref="SensorCollector"/>).</summary>
public sealed class SensorWorker(SensorStore store, ILogger<SensorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reported = new HashSet<string>();
        var collector = new SensorCollector(store,
            [new CpuMemorySource(), new NvidiaSource(), new DiskHealthSource(), new BatterySource()],
            (source, ex) =>
            {
                // One log line per source and run of the service, so a missing sensor does not flood the event log.
                if (reported.Add(source)) logger.LogWarning(ex, "Sensor source {Source} failed; it will be retried at its next turn.", source);
            });

        using var timer = new PeriodicTimer(SensorCollector.Tick);
        do
        {
            try
            {
                collector.RunOnce(DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sensor collection pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
