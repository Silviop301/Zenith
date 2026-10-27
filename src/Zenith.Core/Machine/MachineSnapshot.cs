using System.Management;
using Zenith.Core.Interop;

namespace Zenith.Core;

public sealed record GpuAdapterInfo(
    string Description, string VendorId, GpuKind Kind, ulong DedicatedVideoMemoryMiB, long Luid, IReadOnlyList<string> AttachedOutputs);

public sealed record DisplayInfo(
    string GdiDeviceName, string? MonitorName, [property: System.Text.Json.Serialization.JsonIgnore] string? MonitorDevicePath, long AdapterLuid, uint Width, uint Height,
    double CurrentRefreshHz, int MaxRefreshHzAtCurrentResolution, IReadOnlyList<int> RefreshRatesAtCurrentResolution,
    AdvancedColorState? AdvancedColor);

public sealed record AdvancedColorState(string ApiVersion, bool HdrSupported, bool HdrEnabled, bool LimitedByPolicy, uint BitsPerColorChannel);

public sealed record MemoryModuleInfo(
    string? Manufacturer, string? PartNumber, ulong CapacityMiB, uint? SpeedMHz, uint? ConfiguredClockSpeedMHz, uint? SmbiosMemoryType);

/// <summary>
/// Hardware facts read once per scan and shared by the readers. Each section is read independently,
/// so one failing API does not hide the others; failures are listed in <see cref="CaptureErrors"/>.
/// Nothing personal is collected: no user names, serial numbers or file paths.
/// </summary>
public sealed record MachineSnapshot(
    string? OsName,
    string? OsBuild,
    string? CpuName,
    string? BoardManufacturer,
    string? BoardProduct,
    ChassisKind Chassis,
    IReadOnlyList<int> ChassisTypes,
    IReadOnlyList<GpuAdapterInfo> GpuAdapters,
    IReadOnlyList<DisplayInfo> Displays,
    IReadOnlyList<MemoryModuleInfo> MemoryModules,
    IReadOnlyDictionary<string, string> CaptureErrors)
{
    public bool HasIntegratedGpu => GpuAdapters.Any(a => a.Kind == GpuKind.Integrated);
    public bool HasDiscreteGpu => GpuAdapters.Any(a => a.Kind == GpuKind.Discrete);

    public static MachineSnapshot Capture()
    {
        var errors = new Dictionary<string, string>();

        T Try<T>(string section, Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                errors[section] = $"{ex.GetType().Name}: {ex.Message}";
                return fallback;
            }
        }

        var os = Try("os", () => FirstRow("SELECT Caption, BuildNumber FROM Win32_OperatingSystem"), null);
        var cpu = Try("cpu", () => FirstRow("SELECT Name FROM Win32_Processor"), null);
        var board = Try("board", () => FirstRow("SELECT Manufacturer, Product FROM Win32_BaseBoard"), null);
        var chassisTypes = Try("chassis", ReadChassisTypes, []);
        var gpus = Try("gpu", ReadGpus, []);
        var displays = Try("displays", ReadDisplays, []);
        var memory = Try("memory", ReadMemory, []);

        return new MachineSnapshot(
            Str(os, "Caption"), Str(os, "BuildNumber"), Str(cpu, "Name")?.Trim(),
            Str(board, "Manufacturer"), Str(board, "Product"),
            ChassisClassifier.Classify(chassisTypes), chassisTypes, gpus, displays, memory, errors);
    }

    private static ManagementBaseObject? FirstRow(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        return searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
    }

    private static string? Str(ManagementBaseObject? row, string property) => row?[property]?.ToString();

    private static List<int> ReadChassisTypes()
    {
        using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
        return searcher.Get().Cast<ManagementBaseObject>()
            .SelectMany(r => r["ChassisTypes"] as ushort[] ?? [])
            .Select(t => (int)t)
            .ToList();
    }

    private static List<GpuAdapterInfo> ReadGpus() =>
        Dxgi.EnumerateAdapters()
            // The "Microsoft Basic Render Driver" can appear without the software flag on some systems.
            .Where(a => !a.Description.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
            .Select(a => new GpuAdapterInfo(
                a.Description, $"0x{a.VendorId:X4}", GpuClassifier.Classify(a.VendorId, a.Description, a.DedicatedVideoMemory),
                a.DedicatedVideoMemory / (1024 * 1024), a.Luid, a.AttachedOutputs))
            .ToList();

    private static List<DisplayInfo> ReadDisplays()
    {
        var (paths, modes) = DisplayConfig.QueryActivePaths();
        var result = new List<DisplayInfo>();
        foreach (var path in paths)
        {
            var gdiName = DisplayConfig.GetGdiDeviceName(path.SourceInfo);
            if (gdiName is null) continue;

            uint width = 0, height = 0;
            var modeIdx = path.SourceInfo.ModeInfoIdx;
            if (modeIdx < modes.Length && modes[modeIdx].InfoType == DisplayConfig.ModeInfoTypeSource)
            {
                width = modes[modeIdx].SourceWidth;
                height = modes[modeIdx].SourceHeight;
            }

            var rates = SupportedRefreshRates(gdiName, width, height);
            var targetName = DisplayConfig.GetTargetName(path.TargetInfo);
            result.Add(new DisplayInfo(
                gdiName,
                targetName?.MonitorFriendlyDeviceName,
                targetName?.MonitorDevicePath,
                path.SourceInfo.AdapterId.Value,
                width, height,
                Math.Round(path.TargetInfo.RefreshRate.Value, 2),
                rates.Count > 0 ? rates.Max() : 0,
                rates,
                ReadAdvancedColor(path.TargetInfo)));
        }
        return result;
    }

    /// <summary>Distinct progressive refresh rates the driver offers at the given resolution.</summary>
    private static List<int> SupportedRefreshRates(string gdiName, uint width, uint height)
    {
        var rates = new SortedSet<int>();
        var mode = DisplayConfig.NewDevMode();
        for (var i = 0; DisplayConfig.EnumDisplaySettings(gdiName, i, ref mode); i++)
        {
            if (mode.PelsWidth == width && mode.PelsHeight == height &&
                (mode.DisplayFlags & DisplayConfig.DmInterlaced) == 0 && mode.DisplayFrequency > 1)
                rates.Add((int)mode.DisplayFrequency);
            mode = DisplayConfig.NewDevMode();
        }
        return rates.ToList();
    }

    internal static AdvancedColorState? ReadAdvancedColor(DisplayConfig.PathTargetInfo target)
    {
        var header = new DisplayConfig.DeviceInfoHeader { AdapterId = target.AdapterId, Id = target.Id };

        // Windows 11 24H2 split "advanced color" into HDR and wide color; prefer the newer query.
        var v2 = new DisplayConfig.AdvancedColorInfo2 { Header = header };
        v2.Header.Type = DisplayConfig.DeviceInfoType.GetAdvancedColorInfo2;
        v2.Header.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfig.AdvancedColorInfo2>();
        if (DisplayConfig.GetDeviceInfo(ref v2) == DisplayConfig.ErrorSuccess)
        {
            return new AdvancedColorState("v2",
                HdrSupported: Bit(v2.Value, 4), HdrEnabled: Bit(v2.Value, 5), LimitedByPolicy: Bit(v2.Value, 3),
                v2.BitsPerColorChannel);
        }

        var v1 = new DisplayConfig.AdvancedColorInfo { Header = header };
        v1.Header.Type = DisplayConfig.DeviceInfoType.GetAdvancedColorInfo;
        v1.Header.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfig.AdvancedColorInfo>();
        if (DisplayConfig.GetDeviceInfo(ref v1) == DisplayConfig.ErrorSuccess)
        {
            // Before 24H2, "advanced color supported" on an external monitor means HDR support.
            return new AdvancedColorState("v1",
                HdrSupported: Bit(v1.Value, 0), HdrEnabled: Bit(v1.Value, 1), LimitedByPolicy: Bit(v1.Value, 3),
                v1.BitsPerColorChannel);
        }

        return null;
    }

    private static bool Bit(uint value, int bit) => (value & (1u << bit)) != 0;

    private static List<MemoryModuleInfo> ReadMemory()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        return searcher.Get().Cast<ManagementBaseObject>()
            .Select(r => new MemoryModuleInfo(
                r["Manufacturer"]?.ToString()?.Trim(),
                r["PartNumber"]?.ToString()?.Trim(),
                Convert.ToUInt64(r["Capacity"] ?? 0UL) / (1024 * 1024),
                r["Speed"] is { } s ? Convert.ToUInt32(s) : null,
                r["ConfiguredClockSpeed"] is { } c ? Convert.ToUInt32(c) : null,
                r["SMBIOSMemoryType"] is { } t ? Convert.ToUInt32(t) : null))
            .ToList();
    }
}
