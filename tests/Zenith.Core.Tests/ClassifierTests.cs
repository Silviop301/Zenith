namespace Zenith.Core.Tests;

public class GpuClassifierTests
{
    private const ulong MiB = 1024 * 1024;

    [Theory]
    [InlineData(0x10DEu, "NVIDIA GeForce RTX 3060", 12288ul, GpuKind.Discrete)]
    [InlineData(0x8086u, "Intel(R) UHD Graphics 770", 128ul, GpuKind.Integrated)]
    [InlineData(0x8086u, "Intel(R) Arc(TM) A770 Graphics", 16384ul, GpuKind.Discrete)]
    [InlineData(0x1002u, "AMD Radeon(TM) Graphics", 2048ul, GpuKind.Integrated)]
    [InlineData(0x1002u, "AMD Radeon 780M Graphics", 512ul, GpuKind.Integrated)]
    [InlineData(0x1002u, "AMD Radeon RX 6600", 8176ul, GpuKind.Discrete)]
    public void Classifies_known_adapters(uint vendor, string description, ulong dedicatedMiB, GpuKind expected) =>
        Assert.Equal(expected, GpuClassifier.Classify(vendor, description, dedicatedMiB * MiB));
}

public class ChassisClassifierTests
{
    [Fact] public void Desktop_tower() => Assert.Equal(ChassisKind.Desktop, ChassisClassifier.Classify([3]));
    [Fact] public void Notebook() => Assert.Equal(ChassisKind.Laptop, ChassisClassifier.Classify([10]));
    [Fact] public void Unknown_when_empty() => Assert.Equal(ChassisKind.Unknown, ChassisClassifier.Classify([]));
}
