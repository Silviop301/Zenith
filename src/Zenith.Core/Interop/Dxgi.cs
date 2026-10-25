using System.Runtime.InteropServices;

namespace Zenith.Core.Interop;

/// <summary>
/// Minimal DXGI enumeration through raw COM vtable calls, to avoid a dependency on a full DirectX binding.
/// Vtable slots follow dxgi.h declaration order.
/// </summary>
internal static unsafe class Dxgi
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint AdapterFlagSoftware = 2;

    // IDXGIFactory1: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), EnumAdapters1 = 12
    private const int FactoryEnumAdapters1 = 12;
    // IDXGIAdapter1: IUnknown (0-2), IDXGIObject (3-6), EnumOutputs = 7, GetDesc = 8, CheckInterfaceSupport = 9, GetDesc1 = 10
    private const int AdapterEnumOutputs = 7;
    private const int AdapterGetDesc1 = 10;
    // IDXGIOutput: IUnknown (0-2), IDXGIObject (3-6), GetDesc = 7
    private const int OutputGetDesc = 7;
    private const int UnknownRelease = 2;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public DisplayConfig.Luid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OutputDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public int Left, Top, Right, Bottom;
        public int AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    public sealed record Adapter(
        string Description, uint VendorId, uint DeviceId, ulong DedicatedVideoMemory, ulong SharedSystemMemory,
        long Luid, IReadOnlyList<string> AttachedOutputs);

    /// <summary>Hardware adapters (the software rasterizer is skipped) and the GDI names of their attached outputs.</summary>
    public static IReadOnlyList<Adapter> EnumerateAdapters()
    {
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(IidFactory1, out var factory));
        var result = new List<Adapter>();
        try
        {
            for (uint i = 0; ; i++)
            {
                IntPtr adapter;
                var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(factory, FactoryEnumAdapters1))(factory, i, &adapter);
                if (hr == DxgiErrorNotFound) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var desc = ReadStruct<AdapterDesc1>(adapter, AdapterGetDesc1);
                    if ((desc.Flags & AdapterFlagSoftware) != 0) continue;
                    result.Add(new Adapter(desc.Description, desc.VendorId, desc.DeviceId, desc.DedicatedVideoMemory,
                        desc.SharedSystemMemory, desc.AdapterLuid.Value, EnumerateOutputs(adapter)));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        return result;
    }

    private static List<string> EnumerateOutputs(IntPtr adapter)
    {
        var names = new List<string>();
        for (uint i = 0; ; i++)
        {
            IntPtr output;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(adapter, AdapterEnumOutputs))(adapter, i, &output);
            if (hr == DxgiErrorNotFound) break;
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                var desc = ReadStruct<OutputDesc>(output, OutputGetDesc);
                if (desc.AttachedToDesktop != 0) names.Add(desc.DeviceName);
            }
            finally
            {
                Release(output);
            }
        }
        return names;
    }

    private static T ReadStruct<T>(IntPtr obj, int slot) where T : struct
    {
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VTable(obj, slot))(obj, buffer);
            Marshal.ThrowExceptionForHR(hr);
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr VTable(IntPtr obj, int slot) => (*(IntPtr**)obj)[slot];

    private static void Release(IntPtr obj) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(obj, UnknownRelease))(obj);
}
