using Zenith.Core.Readers;

namespace Zenith.Core.Tests;

public class MemoryRatingTests
{
    [Theory]
    [InlineData("JHD3600U1808JG", 3600u)]
    [InlineData("CMK16GX4M2B3200C16", 3200u)]
    [InlineData("F4-3600C18D-16GTZN", 3600u)]
    [InlineData("KF436C17BBK2/16", 3600u)]
    [InlineData("KF556C40BBK2-32", 5600u)]
    [InlineData("KF560C36BBEK2-32", 6000u)]
    [InlineData("HX432C16FB3/8", 3200u)]
    public void Reads_speed_from_part_number(string partNumber, uint expected) =>
        Assert.Equal(expected, MemoryRating.FromPartNumber(partNumber));

    [Theory]
    [InlineData("HMA81GU6CJR8N-XN")] // OEM module, speed only as a letter code
    [InlineData("M378A1K43EB2-CWE")]
    public void Gives_up_without_a_clear_speed(string partNumber) =>
        Assert.Null(MemoryRating.FromPartNumber(partNumber));

    [Fact]
    public void Knowledge_base_wins_over_part_number()
    {
        var rated = MemoryRating.Lookup("ABC3200X", [("ABC", 3600u)]);
        Assert.Equal(new RatedSpeed(3600, RatedSpeedSource.KnowledgeBase), rated);
    }

    [Fact]
    public void Embedded_knowledge_base_loads() =>
        Assert.Equal(RatedSpeedSource.KnowledgeBase, MemoryRating.Lookup("JHD3600U1808JGRGB").Source);
}

public class GpuUsageParsingTests
{
    [Fact]
    public void Parses_engine_instance_name()
    {
        var instance = GpuUsage.ParseEngineInstance("pid_1234_luid_0x00000001_0x0000C2D1_phys_0_eng_0_engtype_3D");
        Assert.NotNull(instance);
        Assert.Equal(1234, instance.ProcessId);
        Assert.Equal((1L << 32) | 0xC2D1, instance.AdapterLuid);
        Assert.Equal("3D", instance.EngineType);
    }

    [Theory]
    [InlineData("GpuPreference=2;", 2)]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=1;", 1)]
    [InlineData("SwapEffectUpgradeEnable=1;", null)]
    public void Parses_gpu_preference(string value, int? expected) =>
        Assert.Equal(expected, GpuUsage.ParseGpuPreference(value));
}
