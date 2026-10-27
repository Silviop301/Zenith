using System.Text.Json;
using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Ipc;
using Zenith.Core.Settings;

namespace Zenith.Core.Tests;

public class CommandLineTests
{
    private static ParsedCommand? Parse(string line) =>
        CommandLine.Parse(line, s => s.Replace("%LOCALAPPDATA%", @"C:\Users\ana\AppData\Local"));

    [Theory]
    [InlineData("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", @"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe -min", @"C:\Program Files\App\app.exe")]
    [InlineData("%LOCALAPPDATA%\\Discord\\Update.exe --processStart Discord.exe", @"C:\Users\ana\AppData\Local\Discord\Update.exe")]
    [InlineData("C:\\Tools\\tool", @"C:\Tools\tool")]
    [InlineData("ctfmon.exe", "ctfmon.exe")]
    public void Finds_the_executable(string line, string expected) => Assert.Equal(expected, Parse(line)!.Executable);

    [Fact]
    public void A_script_host_points_to_the_script()
    {
        var parsed = Parse(@"wscript.exe //B ""C:\Users\ana\AppData\Roaming\x\run.vbs""")!;

        Assert.Equal("wscript.exe", parsed.Executable);
        Assert.Equal(@"C:\Users\ana\AppData\Roaming\x\run.vbs", parsed.Subject);
    }

    [Fact]
    public void Rundll32_points_to_the_dll() =>
        Assert.Equal(@"C:\Users\ana\AppData\Local\a.dll",
            Parse(@"C:\Windows\System32\rundll32.exe ""C:\Users\ana\AppData\Local\a.dll"",Start")!.Subject);

    [Fact]
    public void A_normal_program_has_no_payload() => Assert.Null(Parse(@"""C:\App\app.exe"" C:\file.js")!.Payload);

    [Fact]
    public void Empty_is_nothing() => Assert.Null(Parse("  "));
}

public class ImpostorDetectorTests
{
    [Theory]
    [InlineData("svch0st.exe", "svchost.exe")]
    [InlineData("scvhost.exe", "svchost.exe")]
    [InlineData("svhost.exe", "svchost.exe")]
    [InlineData("lsas.exe", null)] // short names are only compared after folding look-alikes
    [InlineData("1sass.exe", "lsass.exe")]
    [InlineData("svсhost.exe", "svchost.exe")] // Cyrillic "с"
    [InlineData("expl0rer.exe", "explorer.exe")]
    [InlineData("csrsss.exe", null)]
    [InlineData("winIogon.exe", "winlogon.exe")]
    [InlineData("svchost.exe", null)] // the real name is not an imitation
    [InlineData("steam.exe", null)]
    [InlineData("discord.exe", null)]
    [InlineData("chrome.exe", null)]
    public void Spots_imitations(string name, string? expected) => Assert.Equal(expected, ImpostorDetector.Imitates(name));

    [Fact]
    public void Knows_the_real_names() => Assert.True(ImpostorDetector.IsSystemName("SVCHOST.EXE"));

    [Theory]
    [InlineData("abcdef", "abcdfe", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    public void Distance(string a, string b, int expected) => Assert.Equal(expected, ImpostorDetector.Distance(a, b));
}

public class StartupTargetTests
{
    [Fact]
    public void Round_trips()
    {
        var target = StartupTarget.Format(StartupLocation.MachineRun32, "Steam|x");
        Assert.Equal("machine-run32|Steam|x", target);
        Assert.Equal((StartupLocation.MachineRun32, "Steam|x"), StartupTarget.Parse(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("user-run|")]
    [InlineData("hkcu|Steam")]
    public void Refuses_unknown_targets(string target) => Assert.Throws<ArgumentException>(() => StartupTarget.Parse(target));

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    public void Reads_the_switch(byte[]? flag, bool approved) => Assert.Equal(approved, StartupTarget.IsApproved(flag));

    [Fact]
    public void Writes_the_switch_like_task_manager()
    {
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var off = StartupTarget.ApprovedFlag(false, now);

        Assert.Equal(12, off.Length);
        Assert.Equal(3, off[0]);
        Assert.Equal(now.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.Equal(new byte[12] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, StartupTarget.ApprovedFlag(true, now));
        Assert.False(StartupTarget.IsApproved(off));
    }

    [Theory]
    [InlineData(StartupLocation.UserRun, StartupSetting.UserKey)]
    [InlineData(StartupLocation.UserFolder, StartupSetting.UserKey)]
    [InlineData(StartupLocation.MachineRun, StartupSetting.MachineKey)]
    [InlineData(StartupLocation.MachineFolder, StartupSetting.MachineKey)]
    public void Machine_entries_belong_to_the_service(StartupLocation location, string key) =>
        Assert.Equal(key, StartupSetting.KeyFor(location));
}

public class CatalogTests
{
    [Fact]
    public void The_embedded_catalog_loads_and_is_consistent()
    {
        var catalog = Catalog.Default;

        Assert.True(catalog.Entries.Count >= 60);
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(e => e.Id).Distinct().Count());
        Assert.All(catalog.Entries, e =>
        {
            Assert.NotEmpty(e.Names);
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.What));
        });
    }

    [Fact]
    public void Antivirus_and_defender_are_protectors()
    {
        Assert.Equal(ThreatLevel.Protector, Catalog.Default.Match("MsMpEng.exe", null)!.Level);
        Assert.Equal(ThreatLevel.Protector, Catalog.Default.Match("avastui.exe", null)!.Level);
    }

    [Fact]
    public void Generic_names_need_the_folder()
    {
        Assert.Null(Catalog.Default.Match("Update.exe", @"C:\Users\ana\Downloads\Update.exe"));
        Assert.Equal("discord-update", Catalog.Default.Match("Update.exe", @"C:\Users\ana\AppData\Local\Discord\Update.exe")!.Id);
    }

    [Fact]
    public void Remote_access_asks_for_attention() =>
        Assert.Equal(ThreatLevel.Attention, Catalog.Default.Match("AnyDesk.exe", null)!.Level);
}

public class BestiaryClassifierTests
{
    private static readonly ClassifierContext Context = new(@"C:\Windows\", @"C:\Users\");
    private static readonly Classifier Classifier = new(Catalog.Default, Context);

    private static readonly SignatureInfo Microsoft = new(SignatureStatus.Valid, "Microsoft Windows");
    private static readonly SignatureInfo Unsigned = new(SignatureStatus.Unsigned);

    private static Verdict Classify(string path, SignatureInfo signature, params StartupEntry[] startup) =>
        Classifier.Classify(new Inhabitant(path, CommandLine.FileName(path), null, null, signature, [1], null, null, null, startup));

    private static StartupEntry Run(string command) =>
        new(StartupLocation.UserRun, "x", command, CommandLine.Parse(command, s => s), Enabled: true);

    [Fact]
    public void Windows_signed_by_microsoft_is_a_protector()
    {
        var verdict = Classify(@"C:\Windows\System32\svchost.exe", Microsoft);

        Assert.Equal(ThreatLevel.Protector, verdict.Level);
        Assert.False(verdict.CanStopStartup);
    }

    [Fact]
    public void Windows_folder_unreadable_signature_is_left_alone() =>
        Assert.Equal(ThreatLevel.Protector, Classify(@"C:\Windows\System32\audiodg.exe", SignatureInfo.NotChecked).Level);

    [Fact]
    public void The_mockup_impostor_is_suspicious()
    {
        var verdict = Classify(@"C:\Users\ana\AppData\Roaming\svch0st.exe", Unsigned);

        Assert.Equal(ThreatLevel.Suspicious, verdict.Level);
        Assert.Equal("svchost.exe", verdict.Imitates);
        Assert.Contains(verdict.Reasons, r => r.Kind == ReasonKind.Unsigned);
        Assert.Contains(verdict.Reasons, r => r.Kind == ReasonKind.UserFolder);
    }

    [Fact]
    public void A_signed_program_with_a_similar_name_is_not_an_impostor() =>
        Assert.Null(Classify(@"C:\Program Files\Vendor\svhost.exe", new SignatureInfo(SignatureStatus.Valid, "Vendor Inc.")).Imitates);

    [Fact]
    public void A_system_name_outside_windows_is_suspicious()
    {
        var verdict = Classify(@"C:\ProgramData\lsass.exe", Unsigned);

        Assert.Equal(ThreatLevel.Suspicious, verdict.Level);
        Assert.Contains(verdict.Reasons, r => r.Kind == ReasonKind.SystemNameOutsideWindows);
    }

    [Fact]
    public void A_cataloged_program_signed_by_its_publisher_takes_the_fiche_level()
    {
        var verdict = Classify(@"C:\Program Files (x86)\Steam\steam.exe", new SignatureInfo(SignatureStatus.Valid, "Valve Corp."));

        Assert.Equal(ThreatLevel.Harmless, verdict.Level);
        Assert.Equal("steam", verdict.Fiche!.Id);
        Assert.Contains(verdict.Reasons, r => r is { Kind: ReasonKind.SignedBy, Detail: "Valve Corp." });
    }

    [Fact]
    public void A_cataloged_name_signed_by_someone_else_is_suspicious()
    {
        var verdict = Classify(@"C:\Users\ana\AppData\Local\steam.exe", new SignatureInfo(SignatureStatus.Valid, "Free Tools Ltd"));

        Assert.Equal(ThreatLevel.Suspicious, verdict.Level);
        Assert.Contains(verdict.Reasons, r => r is { Kind: ReasonKind.PublisherMismatch, Detail: "Free Tools Ltd" });
    }

    [Fact]
    public void A_cataloged_name_without_the_expected_signature_is_suspicious() =>
        Assert.Equal(ThreatLevel.Suspicious, Classify(@"C:\Games\Discord.exe", Unsigned).Level);

    [Fact]
    public void A_cataloged_name_whose_signature_could_not_be_read_keeps_the_fiche() =>
        Assert.Equal(ThreatLevel.Harmless, Classify(@"C:\Program Files\Discord\Discord.exe", SignatureInfo.NotChecked).Level);

    [Fact]
    public void A_tampered_signature_is_suspicious() =>
        Assert.Equal(ThreatLevel.Suspicious,
            Classify(@"C:\Program Files\Tool\tool.exe", new SignatureInfo(SignatureStatus.Invalid, "Tool Inc.")).Level);

    [Fact]
    public void Unknown_unsigned_from_a_user_folder_at_startup_needs_attention()
    {
        var path = @"C:\Users\ana\AppData\Local\Temp\abc\helper.exe";
        var verdict = Classify(path, Unsigned, Run($"\"{path}\""));

        Assert.Equal(ThreatLevel.Attention, verdict.Level);
        Assert.Contains(verdict.Reasons, r => r.Kind == ReasonKind.UserFolder);
    }

    [Fact]
    public void Unknown_unsigned_that_does_not_start_with_windows_is_just_unknown() =>
        Assert.Equal(ThreatLevel.Unknown, Classify(@"C:\Users\ana\Downloads\game\game.exe", Unsigned).Level);

    [Fact]
    public void A_script_at_startup_needs_attention()
    {
        var script = @"C:\Users\ana\AppData\Roaming\run.vbs";
        var verdict = Classify(script, Unsigned, Run($@"wscript.exe ""{script}"""));

        Assert.Equal(ThreatLevel.Attention, verdict.Level);
        Assert.Contains(verdict.Reasons, r => r is { Kind: ReasonKind.ScriptAtStartup, Detail: "wscript.exe" });
    }

    [Fact]
    public void Unknown_signed_program_is_unknown_with_its_signer()
    {
        var verdict = Classify(@"C:\Program Files\Acme\acme.exe", new SignatureInfo(SignatureStatus.Valid, "Acme Ltda"));

        Assert.Equal(ThreatLevel.Unknown, verdict.Level);
        Assert.True(verdict.CanStopStartup);
        Assert.Contains(verdict.Reasons, r => r is { Kind: ReasonKind.SignedBy, Detail: "Acme Ltda" });
    }
}

public sealed class StartupCommandTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenith-test-{Guid.NewGuid():N}.db");
    private readonly ChangeJournal _journal;
    private readonly List<string> _disabled = [];
    private readonly CommandHandler _handler;

    public StartupCommandTests()
    {
        _journal = ChangeJournal.Open(_path);
        _handler = new CommandHandler(_journal, new ChangeEngine(_journal, []), () => [], _path,
            startupDisable: target => { _disabled.Add(target); return null; });
    }

    public void Dispose()
    {
        _journal.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    private Response Send(string target, string? sid) => _handler.Handle(
        new Request(1, Protocol.Commands.StartupDisable, JsonSerializer.SerializeToNode(new StartupArgs(target), Protocol.Json)!.AsObject()), sid);

    [Fact]
    public void The_service_turns_off_machine_wide_entries()
    {
        Assert.True(Send("machine-run|Steam", "S-1-5-21-100").Ok);
        Assert.Equal(["machine-run|Steam"], _disabled);
    }

    [Fact]
    public void The_service_leaves_user_entries_to_the_app()
    {
        Assert.False(Send("user-run|Discord", "S-1-5-21-100").Ok);
        Assert.Empty(_disabled);
    }

    [Fact]
    public void An_unidentified_caller_is_refused()
    {
        Assert.False(Send("machine-run|Steam", null).Ok);
        Assert.Empty(_disabled);
    }

    [Fact]
    public void The_agent_may_record_user_startup_changes() =>
        Assert.Contains(StartupSetting.UserKey, CommandHandler.UserSettingKeys);

    [Fact]
    public void The_agent_may_not_record_machine_startup_changes() =>
        Assert.DoesNotContain(StartupSetting.MachineKey, CommandHandler.UserSettingKeys);
}
