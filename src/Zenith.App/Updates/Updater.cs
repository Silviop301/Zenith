using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using Microsoft.Win32;
using Zenith.Core.Updates;

namespace Zenith.App.Updates;

/// <summary>
/// Looks for a new version once a day, downloads it in the background and keeps it ready; installing waits for the
/// user's click. Only an installed Zenith updates itself (a copy run from Downloads or a dev build never does).
/// Releases live in the public repository Silviop301/zenith-versoes; HKLM\Software\Zenith\UpdateFeed can point elsewhere.
/// </summary>
public sealed class Updater
{
    private const string DefaultFeed = "https://github.com/Silviop301/zenith-versoes/releases/latest/download/versao.json";
    private static readonly TimeSpan Every = TimeSpan.FromHours(20);
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "atualizacao");

    public static Version Current { get; } = UpdateManifest.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The installer records where it put Zenith; only that copy updates.</summary>
    public static bool IsInstalledCopy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"Software\Zenith");
        return key?.GetValue("InstallDir") is string dir
            && string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetDirectoryName(Environment.ProcessPath)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The downloaded installer for a newer version, checked against its hash; null when there is none or anything failed.</summary>
    public async Task<(string Version, string Setup)?> CheckAsync(bool force = false)
    {
        try
        {
            if (!IsInstalledCopy()) return null;
            if (!force && DateTimeOffset.UtcNow - LastCheck() < Every) return Ready();

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Zenith/{Current}");
            var manifest = UpdateManifest.Parse(await http.GetStringAsync(FeedUrl()));
            SetLastCheck();
            if (!manifest.IsNewerThan(Current)) return null;

            Directory.CreateDirectory(Folder);
            var setup = Path.Combine(Folder, $"zenith-setup-{manifest.Version}.exe");
            if (!File.Exists(setup) || !UpdateManifest.FileMatches(setup, manifest.Sha256))
            {
                var partial = setup + ".part";
                await using (var source = await http.GetStreamAsync(manifest.Url))
                await using (var target = File.Create(partial))
                    await source.CopyToAsync(target);
                if (new FileInfo(partial).Length != manifest.Size || !UpdateManifest.FileMatches(partial, manifest.Sha256))
                {
                    File.Delete(partial);
                    return null;
                }
                File.Move(partial, setup, overwrite: true);
            }

            foreach (var old in Directory.EnumerateFiles(Folder).Where(f => !f.Equals(setup, StringComparison.OrdinalIgnoreCase)))
                File.Delete(old);
            Remember(manifest.Version, setup, manifest.Sha256);
            return (manifest.Version, setup);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or FormatException
                                       or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Offline, no release yet, or a bad file: try again at the next check.
            return null;
        }
    }

    /// <summary>
    /// Starts the installer quietly; Windows asks for the administrator once. The installer closes Zenith, updates it
    /// and opens it again. Returns false when the user said no to the Windows prompt.
    /// </summary>
    public static bool Install(string setup)
    {
        try
        {
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
    }

    private static string FeedUrl()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"Software\Zenith");
        return key?.GetValue("UpdateFeed") as string ?? DefaultFeed;
    }

    // What was downloaded is remembered so a restart does not wait a day to offer it again.
    private static (string Version, string Setup)? Ready()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Zenith\Update");
        if (key?.GetValue("Version") is not string version || key.GetValue("Setup") is not string setup || key.GetValue("Sha256") is not string sha)
            return null;
        if (UpdateManifest.ParseVersion(version) is not { } v || v <= Current || !File.Exists(setup) || !UpdateManifest.FileMatches(setup, sha))
            return null;
        return (version, setup);
    }

    private static void Remember(string version, string setup, string sha)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Zenith\Update");
        key.SetValue("Version", version);
        key.SetValue("Setup", setup);
        key.SetValue("Sha256", sha);
    }

    private static DateTimeOffset LastCheck()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Zenith\Update");
        return key?.GetValue("LastCheck") is long ticks ? DateTimeOffset.FromUnixTimeSeconds(ticks) : DateTimeOffset.MinValue;
    }

    private static void SetLastCheck()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Zenith\Update");
        key.SetValue("LastCheck", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), RegistryValueKind.QWord);
    }
}
