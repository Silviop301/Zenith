using System.Runtime.InteropServices;
using System.Text;

namespace Zenith.Core.Sensors;

/// <summary>
/// Temperature and use of NVIDIA cards through NVML (nvml.dll), the library the NVIDIA driver installs
/// for nvidia-smi. No driver of our own. PCs without an NVIDIA card or library return nothing.
/// GPU use here is a snapshot of the last second, not an average.
/// </summary>
public sealed unsafe class NvidiaSource : ISensorSource
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;

    private static readonly string[] LibraryPaths =
    [
        "nvml.dll",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
    ];

    public string Name => "gpu-nvidia";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public IEnumerable<SensorSample> Read(DateTimeOffset now)
    {
        if (!TryLoad(out var library)) return [];
        try
        {
            var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
            var shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
            var getCount = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
            var getHandle = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
            var getName = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
            var getTemperature = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
            var getUtilization = (delegate* unmanaged<nint, Utilization*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");

            if (init() != Success) return [];
            try
            {
                uint count;
                if (getCount(&count) != Success) return [];

                var samples = new List<SensorSample>();
                var names = new Dictionary<string, int>();
                var buffer = stackalloc byte[96];
                for (uint i = 0; i < count; i++)
                {
                    nint device;
                    if (getHandle(i, &device) != Success) continue;

                    var name = getName(device, buffer, 96) == Success ? Encoding.UTF8.GetString(buffer, IndexOfNul(buffer, 96)) : $"NVIDIA {i}";
                    // Two identical cards get "(2)" so their histories stay apart.
                    names[name] = names.GetValueOrDefault(name) + 1;
                    var target = names[name] == 1 ? name : $"{name} ({names[name]})";

                    uint temperature;
                    if (getTemperature(device, TemperatureGpu, &temperature) == Success)
                        samples.Add(new SensorSample(SensorKeys.GpuTemperature, target, temperature, now));
                    Utilization use;
                    if (getUtilization(device, &use) == Success)
                        samples.Add(new SensorSample(SensorKeys.GpuUsage, target, use.Gpu, now));
                }
                return samples;
            }
            finally
            {
                shutdown();
            }
        }
        catch (EntryPointNotFoundException)
        {
            return []; // A driver too old for these calls.
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static bool TryLoad(out nint library)
    {
        foreach (var path in LibraryPaths)
        {
            if (NativeLibrary.TryLoad(path, out library)) return true;
        }
        library = 0;
        return false;
    }

    private static int IndexOfNul(byte* buffer, int length)
    {
        for (var i = 0; i < length; i++) if (buffer[i] == 0) return i;
        return length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }
}
