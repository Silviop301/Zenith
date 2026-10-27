using System.Runtime.InteropServices;

namespace Zenith.Core.Interop;

// powrprof.dll, read side only.
internal static class PowerProfile
{
    public static readonly Guid SchemePowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid SchemeBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid SchemeHighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid SchemeUltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid SubgroupProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid ProcessorMaxState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid ProcessorMinState = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    // Power mode slider ("Modo de energia") overlays.
    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayBetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, in Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid, out uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid, uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    public static Guid GetActiveScheme()
    {
        var err = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (err != 0) throw new InvalidOperationException($"PowerGetActiveScheme falhou ({err}).");
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public static string? GetFriendlyName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static uint? ReadAc(Guid scheme, Guid subgroup, Guid setting) =>
        PowerReadACValueIndex(IntPtr.Zero, scheme, subgroup, setting, out var v) == 0 ? v : null;

    public static uint? ReadDc(Guid scheme, Guid subgroup, Guid setting) =>
        PowerReadDCValueIndex(IntPtr.Zero, scheme, subgroup, setting, out var v) == 0 ? v : null;

    public static void SetActiveScheme(Guid scheme)
    {
        var err = PowerSetActiveScheme(IntPtr.Zero, scheme);
        if (err != 0) throw new InvalidOperationException($"PowerSetActiveScheme falhou ({err}).");
    }

    /// <summary>Writes a plugged-in value and re-applies the scheme if it is active, so the change takes effect now.</summary>
    public static void WriteAc(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        var err = PowerWriteACValueIndex(IntPtr.Zero, scheme, subgroup, setting, value);
        if (err != 0) throw new InvalidOperationException($"PowerWriteACValueIndex falhou ({err}).");
        if (GetActiveScheme() == scheme) SetActiveScheme(scheme);
    }

    /// <summary>The power mode slider position, or null where Windows does not expose it.</summary>
    public static Guid? GetActualOverlay()
    {
        try
        {
            return PowerGetActualOverlayScheme(out var g) == 0 ? g : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
