using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Settings;

namespace Zenith.Core.Fixes;

/// <summary>
/// Bestiário: "Impedir de iniciar com o Windows". Turns the entry's switch off, as Task Manager does; the program
/// itself is not closed, changed or removed, and undo turns the switch back on. Used by the agent for this user's
/// entries and by the service for the machine-wide ones.
/// </summary>
public static class StartupFix
{
    /// <summary>The journal's finding id for changes made from the Bestiário.</summary>
    public const string FindingId = "bestiary-startup";

    /// <summary>Returns null when the entry was already off.</summary>
    /// <exception cref="InvalidOperationException">The entry is gone, or it belongs to Windows or the antivirus.</exception>
    public static JournalEntry? Disable(ChangeEngine engine, string target, string? userSid = null, Catalog? catalog = null)
    {
        var (location, name) = StartupTarget.Parse(target);
        var entry = StartupReader.Find(location, name)
            ?? throw new InvalidOperationException("Este programa não está mais na inicialização do Windows.");

        var verdict = Verdict(entry, catalog ?? Catalog.Default, ClassifierContext.Current);
        if (!verdict.CanStopStartup)
            throw new InvalidOperationException("Este programa é do Windows ou da proteção do PC, e o Zenith não mexe nele.");

        return engine.Apply(FindingId, StartupSetting.KeyFor(location), target, StartupSetting.Disabled, userSid);
    }

    /// <summary>The same verdict the Bestiário shows, worked out again here so a request can never skip rule 4.</summary>
    internal static Verdict Verdict(StartupEntry entry, Catalog catalog, ClassifierContext context)
    {
        // An entry whose command cannot be read is treated as Windows' own: better to refuse than to guess.
        if (entry.Parsed is null)
            return new Verdict(ThreatLevel.Protector, null, [new Reason(ReasonKind.SignatureNotChecked)]);
        var subject = entry.Parsed.Subject;
        var path = Path.IsPathRooted(subject) ? subject : Path.Combine(Environment.SystemDirectory, subject);
        var inhabitant = new Inhabitant(path, CommandLine.FileName(path), null, null, BestiaryScanner.Signature(path), [], null, null, null, [entry]);
        return new Classifier(catalog, context).Classify(inhabitant);
    }
}
