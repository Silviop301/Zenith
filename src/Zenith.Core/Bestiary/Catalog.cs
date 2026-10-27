using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zenith.Core.Bestiary;

/// <summary>How much a program deserves the user's attention, from "leave it" to "look now".</summary>
public enum ThreatLevel
{
    /// <summary>Windows itself or the antivirus. Zenith never touches it.</summary>
    Protector,
    Harmless,
    /// <summary>Harmless, and the PC does not need it starting with Windows.</summary>
    Dispensable,
    /// <summary>Not in the catalog and nothing odd about it.</summary>
    Unknown,
    /// <summary>Legitimate in itself but worth knowing about (remote access, unsigned from a user folder).</summary>
    Attention,
    Suspicious,
}

/// <summary>
/// One fiche of the knowledge base. A program matches when its file name is in <see cref="Names"/> and, if
/// <see cref="PathContains"/> is set, its path contains one of those folders (for generic names like "update.exe").
/// <see cref="Publishers"/>, when set, is who must have signed it; a valid signature from anyone else is a red flag.
/// </summary>
public sealed record CatalogEntry(
    string Id,
    IReadOnlyList<string> Names,
    string Title,
    string Category,
    string What,
    ThreatLevel Level,
    IReadOnlyList<string>? Publishers = null,
    IReadOnlyList<string>? PathContains = null,
    string? Startup = null);

/// <summary>The fiches of programs common in Brazil, shipped in KnowledgeBase/bestiario.json and updatable with it.</summary>
public sealed class Catalog
{
    private readonly Dictionary<string, List<CatalogEntry>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public Catalog(IEnumerable<CatalogEntry> entries)
    {
        Entries = entries.ToList();
        foreach (var entry in Entries)
        foreach (var name in entry.Names)
        {
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = [];
            list.Add(entry);
        }
    }

    public IReadOnlyList<CatalogEntry> Entries { get; }

    private static readonly Lazy<Catalog> Embedded = new(() => Load(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("Zenith.Core.KnowledgeBase.bestiario.json")));

    public static Catalog Default => Embedded.Value;

    public static Catalog Load(Stream? stream)
    {
        if (stream is null) return new Catalog([]);
        using (stream)
        {
            var file = JsonSerializer.Deserialize<CatalogFile>(stream, Options);
            return new Catalog(file?.Entries ?? []);
        }
    }

    /// <summary>The fiche for a file; an entry restricted to some folders wins over a general one.</summary>
    public CatalogEntry? Match(string fileName, string? path)
    {
        if (!_byName.TryGetValue(fileName, out var candidates)) return null;
        return candidates
            .Where(e => e.PathContains is not { Count: > 0 } ||
                        path is not null && e.PathContains.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.PathContains?.Count > 0)
            .FirstOrDefault();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private sealed record CatalogFile(List<CatalogEntry> Entries);
}
