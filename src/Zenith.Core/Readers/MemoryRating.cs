using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zenith.Core.Readers;

public enum RatedSpeedSource { KnowledgeBase, PartNumber, Unknown }

public sealed record RatedSpeed(uint? MTs, RatedSpeedSource Source);

/// <summary>
/// Finds a memory module's rated (factory) speed. Windows only reports the speed in use; the XMP/EXPO
/// profile lives on the module's SPD chip, which needs a kernel driver to read. Phase 0 uses a
/// knowledge base of part numbers, then falls back to the speed written in the part number itself.
/// </summary>
public static partial class MemoryRating
{
    private static readonly HashSet<uint> KnownSpeeds =
    [
        2133, 2400, 2666, 2667, 2800, 2933, 3000, 3200, 3300, 3333, 3466, 3600, 3733, 3800, 3866, 4000, 4133, 4266, 4400, 4600,
        4800, 5200, 5600, 6000, 6200, 6400, 6600, 6800, 7000, 7200, 7600, 8000, 8200, 8400,
    ];

    private sealed record KbEntry(string PartNumberPrefix, uint RatedMTs);
    private sealed record KbFile(List<KbEntry> Modules);

    private static readonly Lazy<List<KbEntry>> KnowledgeBase = new(LoadKnowledgeBase);

    // Kingston FURY / HyperX: KF436C17... = DDR4-3600, KF560C40... = DDR5-5600, HX432C16... = DDR4-3200
    [GeneratedRegex(@"^(?:KF|HX)[45](\d{2})C", RegexOptions.IgnoreCase)]
    private static partial Regex KingstonRegex();

    [GeneratedRegex(@"(?<!\d)(\d{4})(?!\d)")]
    private static partial Regex FourDigitsRegex();

    public static RatedSpeed Lookup(string? partNumber) => Lookup(partNumber, KnowledgeBase.Value);

    internal static RatedSpeed Lookup(string? partNumber, IEnumerable<(string Prefix, uint MTs)> knowledgeBase) =>
        Lookup(partNumber, knowledgeBase.Select(e => new KbEntry(e.Prefix, e.MTs)));

    private static RatedSpeed Lookup(string? partNumber, IEnumerable<KbEntry> knowledgeBase)
    {
        var pn = partNumber?.Trim();
        if (string.IsNullOrEmpty(pn)) return new RatedSpeed(null, RatedSpeedSource.Unknown);

        var kb = knowledgeBase
            .Where(e => pn.StartsWith(e.PartNumberPrefix, StringComparison.OrdinalIgnoreCase))
            .MaxBy(e => e.PartNumberPrefix.Length);
        if (kb is not null) return new RatedSpeed(kb.RatedMTs, RatedSpeedSource.KnowledgeBase);

        return FromPartNumber(pn) is { } mts
            ? new RatedSpeed(mts, RatedSpeedSource.PartNumber)
            : new RatedSpeed(null, RatedSpeedSource.Unknown);
    }

    /// <summary>The speed embedded in a part number, only when exactly one plausible speed appears.</summary>
    internal static uint? FromPartNumber(string partNumber)
    {
        var kingston = KingstonRegex().Match(partNumber);
        if (kingston.Success) return uint.Parse(kingston.Groups[1].Value) * 100;

        var candidates = FourDigitsRegex().Matches(partNumber)
            .Select(m => uint.Parse(m.Value))
            .Where(KnownSpeeds.Contains)
            .Distinct()
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static List<KbEntry> LoadKnowledgeBase()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Zenith.Core.KnowledgeBase.memory-modules.json");
        if (stream is null) return [];
        var file = JsonSerializer.Deserialize<KbFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return file?.Modules ?? [];
    }
}
