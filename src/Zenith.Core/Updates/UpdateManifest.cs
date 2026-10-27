using System.Security.Cryptography;
using System.Text.Json;

namespace Zenith.Core.Updates;

/// <summary>
/// versao.json, published with each release next to the installer:
/// { "version": "0.1.42", "url": "https://…/zenith-setup-0.1.42.exe", "sha256": "…", "size": 54000000 }.
/// The hash only proves the download arrived whole; who published it is what code signing will prove later.
/// </summary>
public sealed record UpdateManifest(string Version, string Url, string Sha256, long Size)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <exception cref="FormatException">Missing fields, a version that is not x.y.z, or an address that is not https.</exception>
    public static UpdateManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new FormatException("versao.json vazio.");
        if (ParseVersion(manifest.Version) is null) throw new FormatException($"Versão inválida: {manifest.Version}");
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new FormatException("O endereço do instalador precisa ser https.");
        if (manifest.Sha256 is not { Length: 64 } || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new FormatException("SHA-256 inválido.");
        if (manifest.Size <= 0) throw new FormatException("Tamanho inválido.");
        return manifest;
    }

    public bool IsNewerThan(Version current) => ParseVersion(Version) is { } v && v > Normalize(current);

    /// <summary>"0.1.42" → 0.1.42. Only three numbers, the way the CI numbers releases.</summary>
    public static Version? ParseVersion(string? text) =>
        System.Version.TryParse(text, out var v) && v.Build >= 0 && v.Revision < 0 ? v : null;

    /// <summary>Assembly versions carry a fourth number (0.1.42.0); releases do not.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static bool FileMatches(string path, string sha256)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
