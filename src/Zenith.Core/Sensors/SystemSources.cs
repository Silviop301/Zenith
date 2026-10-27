using System.Management;
using System.Runtime.InteropServices;

namespace Zenith.Core.Sensors;

/// <summary>Sensor names stored in the history. Percentages are 0–100, temperatures °C.</summary>
public static class SensorKeys
{
    public const string CpuUsage = "cpu.usage";
    public const string MemoryUsage = "memory.usage";
    public const string GpuUsage = "gpu.usage";
    public const string GpuTemperature = "gpu.temperature";
    public const string DiskWear = "disk.wear";
    public const string DiskTemperature = "disk.temperature";
    public const string DiskPowerOnHours = "disk.power-on-hours";
    public const string DiskReadErrors = "disk.read-errors-uncorrected";
    public const string BatteryHealth = "battery.health";
    public const string BatteryCycles = "battery.cycles";
}

/// <summary>
/// Average CPU use since the previous reading (not a single instant), from GetSystemTimes, and memory in use
/// from GlobalMemoryStatusEx. The first reading after start only sets the baseline for the CPU.
/// </summary>
public sealed partial class CpuMemorySource : ISensorSource
{
    private (ulong Idle, ulong Kernel, ulong User)? _previous;

    public string Name => "cpu-memory";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public IEnumerable<SensorSample> Read(DateTimeOffset now)
    {
        var samples = new List<SensorSample>();
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var current = (idle, kernel, user);
            if (_previous is { } p && CpuPercent(p, current) is { } cpu)
                samples.Add(new SensorSample(SensorKeys.CpuUsage, "", cpu, now));
            _previous = current;
        }

        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memory))
            samples.Add(new SensorSample(SensorKeys.MemoryUsage, "", memory.MemoryLoad, now));
        return samples;
    }

    /// <summary>Kernel time includes idle time, so busy = kernel + user − idle.</summary>
    internal static double? CpuPercent((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) after)
    {
        var idle = after.Idle - before.Idle;
        var total = after.Kernel - before.Kernel + (after.User - before.User);
        if (total == 0 || idle > total) return null;
        return Math.Round(100.0 * (total - idle) / total, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// <summary>
/// Disk health from Windows' storage reliability counters (the same data as Get-StorageReliabilityCounter,
/// SMART for SATA and the health log for NVMe). Needs administrator rights, so it runs in the service.
/// Fields a drive does not report are skipped, never guessed.
/// </summary>
public sealed class DiskHealthSource : ISensorSource
{
    private const string Scope = @"root\Microsoft\Windows\Storage";

    public string Name => "disk-health";
    public TimeSpan Interval => TimeSpan.FromDays(1);

    public IEnumerable<SensorSample> Read(DateTimeOffset now)
    {
        var names = new Dictionary<string, string>();
        using (var disks = new ManagementObjectSearcher(Scope, "SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk"))
        {
            foreach (var disk in disks.Get().Cast<ManagementBaseObject>())
            {
                var id = disk["DeviceId"]?.ToString();
                if (id is not null) names[id] = DiskTarget(id, disk["FriendlyName"]?.ToString());
            }
        }

        var samples = new List<SensorSample>();
        using var counters = new ManagementObjectSearcher(Scope,
            "SELECT DeviceId, Wear, Temperature, PowerOnHours, ReadErrorsUncorrected FROM MSFT_StorageReliabilityCounter");
        foreach (var counter in counters.Get().Cast<ManagementBaseObject>())
        {
            var id = counter["DeviceId"]?.ToString();
            if (id is null) continue;
            var target = names.GetValueOrDefault(id, DiskTarget(id, null));
            Add(samples, SensorKeys.DiskWear, target, counter["Wear"], now);
            // 0 °C means "not reported" on many drives.
            if (Number(counter["Temperature"]) is double t and > 0) samples.Add(new SensorSample(SensorKeys.DiskTemperature, target, t, now));
            Add(samples, SensorKeys.DiskPowerOnHours, target, counter["PowerOnHours"], now);
            Add(samples, SensorKeys.DiskReadErrors, target, counter["ReadErrorsUncorrected"], now);
        }
        return samples;
    }

    internal static string DiskTarget(string deviceId, string? friendlyName) =>
        string.IsNullOrWhiteSpace(friendlyName) ? $"disco {deviceId}" : $"{friendlyName.Trim()} (disco {deviceId})";

    private static void Add(List<SensorSample> samples, string sensor, string target, object? raw, DateTimeOffset now)
    {
        if (Number(raw) is { } value) samples.Add(new SensorSample(sensor, target, value, now));
    }

    private static double? Number(object? raw) => raw is null ? null : Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Battery wear on notebooks: full charge capacity over design capacity, and charge cycles, from the ACPI
/// battery classes in root\wmi. Desktops have none and return nothing.
/// </summary>
public sealed class BatterySource : ISensorSource
{
    public string Name => "battery";
    public TimeSpan Interval => TimeSpan.FromDays(1);

    public IEnumerable<SensorSample> Read(DateTimeOffset now)
    {
        var design = ByInstance("SELECT InstanceName, DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        var full = ByInstance("SELECT InstanceName, FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
        var cycles = ByInstance("SELECT InstanceName, CycleCount FROM BatteryCycleCount", "CycleCount");

        var samples = new List<SensorSample>();
        var index = 0;
        foreach (var (instance, designed) in design)
        {
            var target = design.Count == 1 ? "bateria" : $"bateria {++index}";
            if (full.TryGetValue(instance, out var charged) && Health(designed, charged) is { } health)
                samples.Add(new SensorSample(SensorKeys.BatteryHealth, target, health, now));
            if (cycles.TryGetValue(instance, out var count) && count > 0)
                samples.Add(new SensorSample(SensorKeys.BatteryCycles, target, count, now));
        }
        return samples;
    }

    /// <summary>How much of the original capacity is left, in percent.</summary>
    internal static double? Health(double designed, double fullCharge) =>
        designed > 0 && fullCharge > 0 ? Math.Round(100 * fullCharge / designed, 1) : null;

    private static Dictionary<string, double> ByInstance(string query, string property)
    {
        var result = new Dictionary<string, double>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", query);
            foreach (var row in searcher.Get().Cast<ManagementBaseObject>())
            {
                if (row["InstanceName"]?.ToString() is { } name && row[property] is { } value)
                    result[name] = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (ManagementException)
        {
            // "Not supported" on machines without an ACPI battery.
        }
        return result;
    }
}
