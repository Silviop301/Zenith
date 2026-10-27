namespace Zenith.Core.Bestiary;

/// <summary>Evidence behind a verdict, each one shown to the user as a sentence.</summary>
public enum ReasonKind
{
    /// <summary>In the Windows folder and signed by Microsoft.</summary>
    WindowsComponent,
    /// <summary>In the Windows folder; the signature could not be checked, so it is left alone.</summary>
    WindowsFolder,
    /// <summary>Signed by <see cref="Reason.Detail"/>, and Windows trusts the signature.</summary>
    SignedBy,
    /// <summary>Signed by <see cref="Reason.Detail"/>, but the fiche says it comes from someone else.</summary>
    PublisherMismatch,
    Unsigned,
    /// <summary>Signed, but the file changed after signing or the certificate is not trusted.</summary>
    InvalidSignature,
    /// <summary>The signature could not be checked (no access to the file).</summary>
    SignatureNotChecked,
    /// <summary>The name imitates the Windows process in <see cref="Reason.Detail"/>.</summary>
    Imitates,
    /// <summary>Has the name of a Windows process but lives outside the Windows folder.</summary>
    SystemNameOutsideWindows,
    /// <summary>Lives in a user folder (AppData, Downloads, Temp), where installed programs rarely go.</summary>
    UserFolder,
    /// <summary>A script started with Windows by the host in <see cref="Reason.Detail"/>.</summary>
    ScriptAtStartup,
}

public sealed record Reason(ReasonKind Kind, string? Detail = null);

/// <param name="Fiche">The catalog fiche, when the program is in it.</param>
public sealed record Verdict(ThreatLevel Level, CatalogEntry? Fiche, IReadOnlyList<Reason> Reasons)
{
    /// <summary>Rule 4: nothing from Windows or the antivirus is stopped, paused or changed.</summary>
    public bool CanStopStartup => Level != ThreatLevel.Protector;

    public string? Imitates => Reasons.FirstOrDefault(r => r.Kind == ReasonKind.Imitates)?.Detail;
}

/// <summary>Folders the classifier compares paths against, e.g. "C:\Windows\" and "C:\Users\".</summary>
public sealed record ClassifierContext(string WindowsDir, string UsersDir)
{
    public static ClassifierContext Current => new(
        WithSlash(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
        WithSlash(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users"));

    private static string WithSlash(string dir) => dir.EndsWith('\\') ? dir : dir + "\\";
}

/// <summary>
/// Decides how much each program deserves attention, from evidence only: the catalog fiche, where the file
/// lives, who signed it and whether its name imitates Windows. Every verdict lists the reasons behind it.
/// </summary>
public sealed class Classifier(Catalog catalog, ClassifierContext context)
{
    public Verdict Classify(Inhabitant inhabitant)
    {
        var path = inhabitant.ExecutablePath;
        var signature = inhabitant.Signature;
        var inWindows = path.StartsWith(context.WindowsDir, StringComparison.OrdinalIgnoreCase);
        var inUserFolder = IsUserFolder(path);
        var reasons = new List<Reason>();

        if (ImpostorDetector.IsSystemName(inhabitant.FileName))
        {
            if (!inWindows)
            {
                reasons.Add(new Reason(ReasonKind.SystemNameOutsideWindows, inhabitant.FileName));
                AddSignature(reasons, signature);
                if (inUserFolder) reasons.Add(new Reason(ReasonKind.UserFolder));
                return new Verdict(signature.IsMicrosoft ? ThreatLevel.Attention : ThreatLevel.Suspicious, null, reasons);
            }
            if (signature.Status is SignatureStatus.Unsigned or SignatureStatus.Invalid)
            {
                AddSignature(reasons, signature);
                return new Verdict(ThreatLevel.Suspicious, null, reasons);
            }
        }

        if (inWindows && signature.IsMicrosoft)
            return new Verdict(ThreatLevel.Protector, catalog.Match(inhabitant.FileName, path), [new Reason(ReasonKind.WindowsComponent)]);
        if (inWindows && signature.Status == SignatureStatus.Unknown)
            return new Verdict(ThreatLevel.Protector, catalog.Match(inhabitant.FileName, path), [new Reason(ReasonKind.WindowsFolder)]);

        // A validly signed program whose name happens to look like a Windows one is not an impostor.
        if (!signature.IsValid && ImpostorDetector.Imitates(inhabitant.FileName) is { } imitated)
        {
            reasons.Add(new Reason(ReasonKind.Imitates, imitated));
            AddSignature(reasons, signature);
            if (inUserFolder) reasons.Add(new Reason(ReasonKind.UserFolder));
            return new Verdict(signature.Status == SignatureStatus.Unknown ? ThreatLevel.Attention : ThreatLevel.Suspicious, null, reasons);
        }

        var fiche = catalog.Match(inhabitant.FileName, path);
        var level = fiche?.Level ?? ThreatLevel.Unknown;

        if (fiche?.Publishers is { Count: > 0 } publishers)
        {
            var fromPublisher = signature.Signer is { } signer &&
                                publishers.Any(p => signer.Contains(p, StringComparison.OrdinalIgnoreCase));
            switch (signature.Status)
            {
                case SignatureStatus.Valid when fromPublisher:
                    reasons.Add(new Reason(ReasonKind.SignedBy, signature.Signer));
                    break;
                case SignatureStatus.Valid:
                    reasons.Add(new Reason(ReasonKind.PublisherMismatch, signature.Signer));
                    level = ThreatLevel.Suspicious;
                    break;
                case SignatureStatus.Unsigned or SignatureStatus.Invalid:
                    AddSignature(reasons, signature);
                    level = ThreatLevel.Suspicious;
                    break;
                default:
                    reasons.Add(new Reason(ReasonKind.SignatureNotChecked));
                    break;
            }
        }
        else
        {
            AddSignature(reasons, signature);
            if (signature.Status == SignatureStatus.Invalid) level = ThreatLevel.Suspicious;
        }

        if (inhabitant.Startup.FirstOrDefault(s => s.Enabled && s.Parsed?.Payload is not null &&
                                                    CommandLine.ScriptHosts.Contains(CommandLine.FileName(s.Parsed.Executable))) is { } script)
        {
            reasons.Add(new Reason(ReasonKind.ScriptAtStartup, CommandLine.FileName(script.Parsed!.Executable)));
            level = Worst(level, ThreatLevel.Attention);
        }

        if (inUserFolder && fiche is null)
        {
            reasons.Add(new Reason(ReasonKind.UserFolder));
            if (signature.Status != SignatureStatus.Valid && inhabitant.StartsWithWindows) level = Worst(level, ThreatLevel.Attention);
        }

        return new Verdict(level, fiche, reasons);
    }

    private bool IsUserFolder(string path) =>
        path.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(context.UsersDir, StringComparison.OrdinalIgnoreCase);

    private static void AddSignature(List<Reason> reasons, SignatureInfo signature) => reasons.Add(signature.Status switch
    {
        SignatureStatus.Valid => new Reason(ReasonKind.SignedBy, signature.Signer),
        SignatureStatus.Unsigned => new Reason(ReasonKind.Unsigned),
        SignatureStatus.Invalid => new Reason(ReasonKind.InvalidSignature, signature.Signer),
        _ => new Reason(ReasonKind.SignatureNotChecked),
    });

    private static ThreatLevel Worst(ThreatLevel a, ThreatLevel b) => a > b ? a : b;
}
