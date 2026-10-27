using Zenith.Core.Updates;

namespace Zenith.Core.Tests;

public sealed class UpdateTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Json(string version = "0.1.42", string url = "https://github.com/x/zenith-setup.exe", string sha = Hash, long size = 100) =>
        $$"""{ "version": "{{version}}", "url": "{{url}}", "sha256": "{{sha}}", "size": {{size}} }""";

    [Fact]
    public void A_valid_manifest_is_read_and_compared_with_the_installed_version()
    {
        var manifest = UpdateManifest.Parse(Json());
        Assert.True(manifest.IsNewerThan(new Version(0, 1, 41, 0)));
        Assert.False(manifest.IsNewerThan(new Version(0, 1, 42, 0)));
        Assert.False(manifest.IsNewerThan(new Version(0, 2, 0)));
    }

    [Theory]
    [InlineData("0.1", "https://a/b.exe", Hash)]
    [InlineData("0.1.2.3", "https://a/b.exe", Hash)]
    [InlineData("0.1.42", "http://a/b.exe", Hash)]
    [InlineData("0.1.42", "https://a/b.exe", "abc")]
    public void A_manifest_with_a_bad_version_address_or_hash_is_refused(string version, string url, string sha)
    {
        Assert.Throws<FormatException>(() => UpdateManifest.Parse(Json(version, url, sha)));
    }

    [Fact]
    public void The_download_is_checked_against_its_hash()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "zenith");
            // sha256("zenith")
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("zenith"u8));
            Assert.True(UpdateManifest.FileMatches(path, hash.ToLowerInvariant()));
            Assert.False(UpdateManifest.FileMatches(path, Hash));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
