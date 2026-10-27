using Zenith.Core.Sensors;

namespace Zenith.Core.Tests;

public sealed class SensorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenith-sensors-{Guid.NewGuid():N}.db");
    private readonly SensorStore _store;
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public SensorTests() => _store = SensorStore.Open(_path);

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" }) File.Delete(f);
    }

    private sealed class FakeSource(string name, TimeSpan interval, Func<DateTimeOffset, IEnumerable<SensorSample>> read) : ISensorSource
    {
        public int Runs { get; private set; }
        public string Name => name;
        public TimeSpan Interval => interval;
        public IEnumerable<SensorSample> Read(DateTimeOffset now) { Runs++; return read(now); }
    }

    [Fact]
    public void Latest_keeps_the_newest_reading_per_sensor_and_target()
    {
        _store.Add([
            new("gpu.temperature", "RTX 3060", 50, Now.AddMinutes(-10)),
            new("gpu.temperature", "RTX 3060", 61, Now),
            new("cpu.usage", "", 12, Now.AddMinutes(-5)),
        ]);
        var latest = _store.Latest();
        Assert.Equal(2, latest.Count);
        Assert.Equal(61, latest.Single(s => s.Sensor == "gpu.temperature").Value);
    }

    [Fact]
    public void Prune_drops_readings_older_than_a_year()
    {
        _store.Add([new("cpu.usage", "", 1, Now.AddDays(-400)), new("cpu.usage", "", 2, Now.AddDays(-10))]);
        Assert.Equal(1, _store.Prune(Now));
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void Sources_run_at_their_own_interval_across_restarts()
    {
        var fast = new FakeSource("fast", TimeSpan.FromMinutes(5), n => [new("cpu.usage", "", 1, n)]);
        var daily = new FakeSource("daily", TimeSpan.FromDays(1), n => [new("disk.wear", "disco 0", 3, n)]);
        var collector = new SensorCollector(_store, [fast, daily]);

        collector.RunOnce(Now);
        collector.RunOnce(Now.AddMinutes(1));
        collector.RunOnce(Now.AddMinutes(5));
        Assert.Equal(2, fast.Runs);
        Assert.Equal(1, daily.Runs);

        // A new collector (service restarted) remembers the daily source already ran.
        new SensorCollector(_store, [daily]).RunOnce(Now.AddHours(2));
        Assert.Equal(1, daily.Runs);
        new SensorCollector(_store, [daily]).RunOnce(Now.AddDays(1));
        Assert.Equal(2, daily.Runs);
    }

    [Fact]
    public void A_daily_source_that_finds_nothing_is_tried_again_within_the_hour()
    {
        var found = false;
        var disk = new FakeSource("disk", TimeSpan.FromDays(1), n => found ? [new("disk.wear", "disco 0", 3, n)] : []);
        var collector = new SensorCollector(_store, [disk]);

        collector.RunOnce(Now);
        collector.RunOnce(Now.AddMinutes(30));
        Assert.Equal(1, disk.Runs);
        found = true;
        collector.RunOnce(Now.AddHours(1));
        Assert.Equal(2, disk.Runs);
        // With readings stored it goes back to once a day.
        collector.RunOnce(Now.AddHours(3));
        Assert.Equal(2, disk.Runs);
    }

    [Fact]
    public void A_failing_source_does_not_stop_the_others_and_waits_its_turn()
    {
        var errors = new List<string>();
        var broken = new FakeSource("broken", TimeSpan.FromMinutes(5), _ => throw new InvalidOperationException("sem sensor"));
        var ok = new FakeSource("ok", TimeSpan.FromMinutes(5), n => [new("cpu.usage", "", 1, n)]);
        var collector = new SensorCollector(_store, [broken, ok], (name, _) => errors.Add(name));

        Assert.Equal(1, collector.RunOnce(Now));
        collector.RunOnce(Now.AddMinutes(1));
        Assert.Equal(["broken"], errors);
        Assert.Equal(1, broken.Runs);
    }

    [Theory]
    [InlineData(100ul, 1000ul, 1000ul, 150ul, 1500ul, 1500ul, 95.0)]  // 1000 ticks, 50 idle
    [InlineData(100ul, 1000ul, 1000ul, 600ul, 1500ul, 1000ul, 0.0)]   // all idle
    public void Cpu_use_is_the_average_between_readings(ulong i0, ulong k0, ulong u0, ulong i1, ulong k1, ulong u1, double expected) =>
        Assert.Equal(expected, CpuMemorySource.CpuPercent((i0, k0, u0), (i1, k1, u1)));

    [Fact]
    public void No_time_passed_gives_no_cpu_reading() =>
        Assert.Null(CpuMemorySource.CpuPercent((1, 2, 3), (1, 2, 3)));

    [Theory]
    [InlineData(50000, 42500, 85.0)]
    [InlineData(0, 42500, null)]
    public void Battery_health_is_full_charge_over_design(double design, double full, double? expected) =>
        Assert.Equal(expected, BatterySource.Health(design, full));

    [Fact]
    public void Disk_names_include_the_disk_number() =>
        Assert.Equal("Samsung SSD 980 1TB (disco 0)", DiskHealthSource.DiskTarget("0", "Samsung SSD 980 1TB "));
}
