namespace Zenith.Core.Sensors;

/// <summary>Something that can be read on a schedule: CPU and memory, the GPU, the disks, the battery.</summary>
public interface ISensorSource
{
    string Name { get; }
    TimeSpan Interval { get; }
    /// <summary>Reads now. Returns nothing when the hardware is not there (no battery on a desktop, no NVIDIA card).</summary>
    IEnumerable<SensorSample> Read(DateTimeOffset now);
}

/// <summary>
/// Runs each source when it is due and stores what it read. Silent by design: a source that fails is
/// reported to <paramref name="onError"/> and tried again at its next turn; it never stops the others.
/// </summary>
public sealed class SensorCollector(SensorStore store, IReadOnlyList<ISensorSource> sources, Action<string, Exception>? onError = null)
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    /// <summary>Sources whose last run is at least one interval ago, or that never ran.</summary>
    public static IEnumerable<ISensorSource> Due(IEnumerable<ISensorSource> sources, IReadOnlyDictionary<string, DateTimeOffset> lastRuns, DateTimeOffset now) =>
        sources.Where(s => !lastRuns.TryGetValue(s.Name, out var last) || now - last >= s.Interval - TimeSpan.FromSeconds(5) || last > now);

    /// <summary>One pass: reads every due source and prunes old readings once a day. Returns how many readings were stored.</summary>
    public int RunOnce(DateTimeOffset now)
    {
        var stored = 0;
        foreach (var source in Due(sources, store.LastRuns(), now))
        {
            try
            {
                var samples = source.Read(now).ToList();
                store.Add(samples);
                stored += samples.Count;
            }
            catch (Exception ex)
            {
                onError?.Invoke(source.Name, ex);
            }
            // Marked even after a failure, so a broken source is retried at its interval, not every minute.
            store.MarkRun(source.Name, now);
        }

        if (now - _lastPrune >= TimeSpan.FromDays(1))
        {
            store.Prune(now);
            _lastPrune = now;
        }
        return stored;
    }
}
