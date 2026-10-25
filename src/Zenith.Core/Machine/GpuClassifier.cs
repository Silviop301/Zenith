namespace Zenith.Core;

public enum GpuKind { Integrated, Discrete }

/// <summary>
/// Decides whether a GPU is integrated (shares system memory) or discrete. DXGI has no flag for this,
/// so it is a heuristic; the Phase 0 collector exists partly to validate it on varied hardware.
/// </summary>
public static class GpuClassifier
{
    public const uint VendorIntel = 0x8086;
    public const uint VendorAmd = 0x1002;
    public const uint VendorNvidia = 0x10DE;

    private const ulong MiB = 1024 * 1024;

    public static GpuKind Classify(uint vendorId, string description, ulong dedicatedVideoMemory)
    {
        // NVIDIA only ships discrete GPUs on PCs.
        if (vendorId == VendorNvidia) return GpuKind.Discrete;

        // Intel Arc discrete cards report several GiB; Intel iGPUs report 128 MiB or less.
        if (vendorId == VendorIntel) return dedicatedVideoMemory >= 2048 * MiB ? GpuKind.Discrete : GpuKind.Integrated;

        if (vendorId == VendorAmd)
        {
            // Ryzen APUs show up as "AMD Radeon(TM) Graphics" or "Radeon 780M"-style names and may reserve
            // up to a few GiB of system RAM as "dedicated", so memory size alone is not enough.
            if (description.Contains(" RX ", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase))
                return GpuKind.Discrete;
            if (description.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                description.EndsWith("Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(description, @"Radeon\s+\d{3}M\b"))
                return GpuKind.Integrated;
        }

        return dedicatedVideoMemory >= 1024 * MiB ? GpuKind.Discrete : GpuKind.Integrated;
    }
}
