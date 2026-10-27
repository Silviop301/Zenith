using System.Globalization;
using Zenith.Core.Bestiary;
using Zenith.Core.Resources;

namespace Zenith.Core.Presentation;

public enum BestiaryFilter { All, Startup, Heavy, Unknown, Attention }

/// <param name="Value">"410 MB", "12%"; <see cref="Strings.StatNone"/> when Windows did not let Zenith read it.</param>
public sealed record FicheStat(string Value, string Label);

/// <summary>One inhabitant as the Bestiário shows it: the card in the grid and the fiche on the side.</summary>
public sealed record InhabitantCard(
    string Path,
    string Initials,
    string Name,
    string FileName,
    string Category,
    ThreatLevel Level,
    string LevelLabel,
    string Cost,
    string? ImpostorChip,
    string Who,
    string ReasonsTitle,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<FicheStat> Stats,
    string? StartupAdvice,
    IReadOnlyList<string> StopTargets,
    bool StartsWithWindows,
    bool IsHeavy,
    bool IsCataloged)
{
    public bool CanStopStartup => StopTargets.Count > 0;

    public bool Matches(BestiaryFilter filter) => filter switch
    {
        BestiaryFilter.Startup => StartsWithWindows,
        BestiaryFilter.Heavy => IsHeavy,
        BestiaryFilter.Unknown => Level == ThreatLevel.Unknown,
        BestiaryFilter.Attention => Level >= ThreatLevel.Attention,
        _ => true,
    };

    public bool Matches(string? search) =>
        string.IsNullOrWhiteSpace(search) ||
        Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
        FileName.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
        Category.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

public sealed record BestiaryOverview(
    string Eyebrow,
    int Total,
    int Cataloged,
    string CatalogedLabel,
    string CatalogedPercent,
    double CatalogedFraction,
    IReadOnlyDictionary<BestiaryFilter, int> Counts,
    string? HiddenNote);

/// <summary>Turns a Bestiário scan into cards and fiches. Every statement on a fiche comes from a reason the classifier recorded.</summary>
public static class BestiaryPresenter
{
    /// <summary>"Pesados": a program using this much memory or processor right now.</summary>
    public const long HeavyMemoryBytes = 500L * 1024 * 1024;
    public const double HeavyCpuPercent = 10;

    /// <summary>Most worrying first, then the heaviest; programs that only start with Windows go last.</summary>
    public static IReadOnlyList<InhabitantCard> Cards(BestiaryScan scan) =>
        scan.Sightings
            .OrderByDescending(s => s.Verdict.Level >= ThreatLevel.Attention ? (int)s.Verdict.Level : 0)
            .ThenByDescending(s => s.Inhabitant.IsRunning)
            .ThenByDescending(s => s.Inhabitant.MemoryBytes ?? 0)
            .Select(Card)
            .ToList();

    public static BestiaryOverview Overview(IReadOnlyList<InhabitantCard> cards, int hiddenProcesses)
    {
        var cataloged = cards.Count(c => c.IsCataloged);
        var fraction = cards.Count == 0 ? 0 : (double)cataloged / cards.Count;
        return new BestiaryOverview(
            FindingPresenter.Plural(cards.Count, Strings.BestiaryEyebrow_One, Strings.BestiaryEyebrow_Other),
            cards.Count,
            cataloged,
            F(Strings.BestiaryCataloged, cataloged, cards.Count),
            F(Strings.BestiaryCatalogedPercent, fraction * 100),
            fraction,
            Enum.GetValues<BestiaryFilter>().ToDictionary(f => f, f => cards.Count(c => c.Matches(f))),
            hiddenProcesses == 0 ? null : FindingPresenter.Plural(hiddenProcesses, Strings.BestiaryHidden_One, Strings.BestiaryHidden_Other));
    }

    public static InhabitantCard Card(Sighting sighting)
    {
        var (inhabitant, verdict) = sighting;
        var fiche = verdict.Fiche;
        var suspicious = verdict.Level == ThreatLevel.Suspicious;

        // A suspicious file is named by its file name: its self-description is exactly what an impostor would fake.
        var name = suspicious ? inhabitant.FileName : fiche?.Title ?? inhabitant.Description ?? inhabitant.FileName;
        var category = fiche?.Category
            ?? (verdict.Level == ThreatLevel.Protector ? Strings.CategoryWindows : Strings.CategoryUnknown);

        var heavy = inhabitant.MemoryBytes >= HeavyMemoryBytes || inhabitant.CpuPercent >= HeavyCpuPercent;
        var stopTargets = verdict.CanStopStartup
            ? inhabitant.Startup.Where(s => s.Enabled).Select(s => s.Target).ToList()
            : [];

        return new InhabitantCard(
            inhabitant.ExecutablePath,
            suspicious || verdict.Level == ThreatLevel.Attention && fiche is null ? "?" : Initials(name),
            name,
            inhabitant.FileName,
            category,
            verdict.Level,
            LevelLabel(verdict.Level),
            Cost(inhabitant),
            verdict.Imitates is not null ? Strings.ImpostorChip : null,
            Who(inhabitant, verdict),
            suspicious ? Strings.FicheWhy : Strings.FicheSeen,
            verdict.Reasons.Select(r => Reason(r, fiche)).ToList(),
            [
                new(inhabitant.MemoryBytes is { } m ? Bytes(m) : Strings.StatNone, Strings.StatMemory),
                new(inhabitant.CpuPercent is { } c ? Percent(c) : Strings.StatNone, Strings.StatCpu),
                new(inhabitant.RemoteHosts is { } n ? n.ToString(CultureInfo.CurrentCulture) : Strings.StatNone, Strings.StatNetwork),
                new(inhabitant.StartsWithWindows ? Strings.StatYes : Strings.StatNo, Strings.StatStartup),
            ],
            StartupAdvice(inhabitant, verdict),
            stopTargets,
            inhabitant.StartsWithWindows,
            heavy,
            fiche is not null || verdict.Level == ThreatLevel.Protector);
    }

    public static string LevelLabel(ThreatLevel level) => level switch
    {
        ThreatLevel.Protector => Strings.LevelProtector,
        ThreatLevel.Harmless => Strings.LevelHarmless,
        ThreatLevel.Dispensable => Strings.LevelDispensable,
        ThreatLevel.Attention => Strings.LevelAttention,
        ThreatLevel.Suspicious => Strings.LevelSuspicious,
        _ => Strings.LevelUnknown,
    };

    private static string Cost(Inhabitant inhabitant)
    {
        if (inhabitant.IsRunning)
        {
            var memory = inhabitant.MemoryBytes is { } m ? Bytes(m) : Strings.CardNotMeasured;
            return inhabitant.StartsWithWindows ? F(Strings.CardRunningStartup, memory) : memory;
        }
        return inhabitant.StartsWithWindows ? Strings.CardStartupOnly : Strings.CardStartupOff;
    }

    private static string Who(Inhabitant inhabitant, Verdict verdict)
    {
        if (verdict.Imitates is { } imitated) return F(Strings.WhoImitates, imitated);
        if (verdict.Fiche is { } fiche) return fiche.What;
        if (verdict.Level == ThreatLevel.Protector) return Strings.WhoWindows;
        return (inhabitant.Description, inhabitant.Company) switch
        {
            ({ } d, { } c) => F(Strings.WhoDescribedBy, d, c),
            ({ } d, null) => F(Strings.WhoDescribed, d),
            _ => Strings.WhoNothing,
        };
    }

    private static string Reason(Reason reason, CatalogEntry? fiche) => reason.Kind switch
    {
        ReasonKind.WindowsComponent => Strings.ReasonWindowsComponent,
        ReasonKind.WindowsFolder => Strings.ReasonWindowsFolder,
        ReasonKind.SignedBy => F(Strings.ReasonSignedBy, reason.Detail ?? "?"),
        ReasonKind.PublisherMismatch => F(Strings.ReasonPublisherMismatch, reason.Detail ?? "?", fiche?.Title ?? "?"),
        ReasonKind.Unsigned when fiche?.Publishers is { Count: > 0 } => F(Strings.ReasonUnsignedExpected, fiche.Title),
        ReasonKind.Unsigned => Strings.ReasonUnsigned,
        ReasonKind.InvalidSignature => Strings.ReasonInvalidSignature,
        ReasonKind.Imitates => F(Strings.ReasonImitates, reason.Detail ?? "?"),
        ReasonKind.SystemNameOutsideWindows => F(Strings.ReasonSystemNameOutsideWindows, reason.Detail ?? "?"),
        ReasonKind.UserFolder => Strings.ReasonUserFolder,
        ReasonKind.ScriptAtStartup => F(Strings.ReasonScriptAtStartup, reason.Detail ?? "?"),
        _ => Strings.ReasonSignatureNotChecked,
    };

    private static string? StartupAdvice(Inhabitant inhabitant, Verdict verdict)
    {
        if (!verdict.CanStopStartup) return inhabitant.Startup.Count > 0 ? Strings.StartupProtected : null;
        if (inhabitant.StartsWithWindows) return verdict.Fiche?.Startup ?? Strings.StartupNextSignIn;
        return inhabitant.Startup.Count > 0 ? Strings.StartupOffNote : null;
    }

    internal static string Initials(string name)
    {
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var words = name.Split([' ', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        if (words.Count == 0) return "?";
        var initials = words.Count >= 2 ? $"{words[0][0]}{words[1][0]}" : words[0][..Math.Min(2, words[0].Length)];
        return initials.ToUpper(CultureInfo.CurrentCulture);
    }

    internal static string Bytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? F(Strings.StatGigabytes, bytes / (1024.0 * 1024 * 1024))
        : F(Strings.StatMegabytes, Math.Max(1, bytes / (1024.0 * 1024)));

    internal static string Percent(double percent) => percent is > 0 and < 1 ? Strings.StatLessThanOne : F(Strings.StatPercent, percent);

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
