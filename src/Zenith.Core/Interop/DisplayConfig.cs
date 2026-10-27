using System.Runtime.InteropServices;

namespace Zenith.Core.Interop;

// Windows display configuration API (wingdi.h / winuser.h). Only the read side is declared here.
internal static partial class DisplayConfig
{
    public const uint QdcOnlyActivePaths = 0x2;
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    public enum DeviceInfoType : uint
    {
        GetSourceName = 1,
        GetTargetName = 2,
        GetAdvancedColorInfo = 9,
        SetAdvancedColorState = 10,
        GetAdvancedColorInfo2 = 15, // Windows 11 24H2+
        SetHdrState = 16, // Windows 11 24H2+
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SetEnableState
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit 0: enable
    }

    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
    public static extern int SetDeviceInfo(ref SetEnableState request);

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly long Value => ((long)HighPart << 32) | LowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;

        public readonly double Value => Denominator == 0 ? 0 : (double)Numerator / Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO: the union is 48 bytes; only the source-mode size is read.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ModeInfo
    {
        [FieldOffset(0)] public uint InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid AdapterId;
        [FieldOffset(16)] public uint SourceWidth;
        [FieldOffset(20)] public uint SourceHeight;
    }

    public const uint ModeInfoTypeSource = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader
    {
        public DeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit 0 supported, bit 1 enabled, bit 2 wide color enforced, bit 3 force disabled
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo2
    {
        public DeviceInfoHeader Header;
        // bit 0 advanced color supported, 1 active, 3 limited by policy,
        // 4 HDR supported, 5 HDR user enabled, 6 wide color supported, 7 wide color user enabled
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] PathInfo[] pathArray,
        ref uint numModeInfoArrayElements, [Out] ModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetDeviceInfo(ref AdvancedColorInfo request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetDeviceInfo(ref AdvancedColorInfo2 request);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    public const uint DmInterlaced = 0x2;
    public const int EnumCurrentSettings = -1;
    public const uint DmPelsWidth = 0x80000;
    public const uint DmPelsHeight = 0x100000;
    public const uint DmDisplayFrequency = 0x400000;
    public const uint CdsUpdateRegistry = 0x1;
    public const int DispChangeSuccessful = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    public static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    public static DevMode NewDevMode() => new() { Size = (ushort)Marshal.SizeOf<DevMode>(), DeviceName = "", FormName = "" };

    /// <summary>Active display paths with their mode table, retrying if the topology changes between calls.</summary>
    public static (PathInfo[] Paths, ModeInfo[] Modes) QueryActivePaths()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var err = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var numPaths, out var numModes);
            if (err != ErrorSuccess) throw new InvalidOperationException($"GetDisplayConfigBufferSizes falhou ({err}).");

            var paths = new PathInfo[numPaths];
            var modes = new ModeInfo[numModes];
            err = QueryDisplayConfig(QdcOnlyActivePaths, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
            if (err == ErrorInsufficientBuffer) continue;
            if (err != ErrorSuccess) throw new InvalidOperationException($"QueryDisplayConfig falhou ({err}).");
            return (paths[..(int)numPaths], modes[..(int)numModes]);
        }
        throw new InvalidOperationException("A configuração de tela mudou durante a leitura.");
    }

    public static string? GetGdiDeviceName(in PathSourceInfo source)
    {
        var request = new SourceDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoType.GetSourceName,
                Size = (uint)Marshal.SizeOf<SourceDeviceName>(),
                AdapterId = source.AdapterId,
                Id = source.Id,
            },
            ViewGdiDeviceName = "",
        };
        return GetDeviceInfo(ref request) == ErrorSuccess ? request.ViewGdiDeviceName : null;
    }

    public static TargetDeviceName? GetTargetName(in PathTargetInfo target)
    {
        var request = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoType.GetTargetName,
                Size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                AdapterId = target.AdapterId,
                Id = target.Id,
            },
            MonitorFriendlyDeviceName = "",
            MonitorDevicePath = "",
        };
        return GetDeviceInfo(ref request) == ErrorSuccess ? request : null;
    }
}
