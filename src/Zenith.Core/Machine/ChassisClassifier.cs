namespace Zenith.Core;

public enum ChassisKind { Desktop, Laptop, Unknown }

public static class ChassisClassifier
{
    // SMBIOS chassis types (Win32_SystemEnclosure.ChassisTypes).
    private static readonly HashSet<int> DesktopTypes = [3, 4, 5, 6, 7, 13, 15, 16, 24, 35, 36];
    private static readonly HashSet<int> LaptopTypes = [8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32];

    public static ChassisKind Classify(IEnumerable<int> chassisTypes)
    {
        var types = chassisTypes.ToList();
        if (types.Any(LaptopTypes.Contains)) return ChassisKind.Laptop;
        if (types.Any(DesktopTypes.Contains)) return ChassisKind.Desktop;
        return ChassisKind.Unknown;
    }
}
