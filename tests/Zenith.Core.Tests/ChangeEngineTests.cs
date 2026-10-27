using Zenith.Core.Changes;

namespace Zenith.Core.Tests;

internal sealed class FakeSetting(string key, string initial, ChangeScope scope = ChangeScope.System) : ISetting
{
    public string Value { get; set; } = initial;
    public bool RejectWrites { get; set; }

    public string Key => key;
    public ChangeScope Scope => scope;
    public string Read(string target) => Value;

    public void Write(string target, string value)
    {
        if (!RejectWrites) Value = value;
    }
}

public sealed class ChangeEngineTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenith-test-{Guid.NewGuid():N}.db");
    private readonly ChangeJournal _journal;

    public ChangeEngineTests() => _journal = ChangeJournal.Open(_path);

    public void Dispose()
    {
        _journal.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    [Fact]
    public void Apply_records_previous_and_new_value()
    {
        var setting = new FakeSetting("power.test", "a");
        var engine = new ChangeEngine(_journal, [setting]);

        var entry = engine.Apply("finding", "power.test", "machine", "b");

        Assert.NotNull(entry);
        Assert.Equal("b", setting.Value);
        Assert.Equal(("a", "b", JournalStatus.Applied), (entry.PreviousValue, entry.NewValue, entry.Status));
    }

    [Fact]
    public void Apply_does_nothing_when_value_is_already_right()
    {
        var engine = new ChangeEngine(_journal, [new FakeSetting("power.test", "b")]);

        Assert.Null(engine.Apply("finding", "power.test", "machine", "b"));
        Assert.Empty(_journal.List());
    }

    [Fact]
    public void Apply_fails_and_records_nothing_when_windows_ignores_the_write()
    {
        var engine = new ChangeEngine(_journal, [new FakeSetting("power.test", "a") { RejectWrites = true }]);

        Assert.Throws<InvalidOperationException>(() => engine.Apply("finding", "power.test", "machine", "b"));
        Assert.Empty(_journal.List());
    }

    [Fact]
    public void Undo_restores_previous_value()
    {
        var setting = new FakeSetting("power.test", "a");
        var engine = new ChangeEngine(_journal, [setting]);
        var entry = engine.Apply("finding", "power.test", "machine", "b")!;

        var result = engine.Undo(entry.Id);

        Assert.Equal(JournalStatus.Undone, result.Status);
        Assert.Equal("a", setting.Value);
        Assert.Equal(JournalStatus.Undone, _journal.Get(entry.Id)!.Status);
    }

    [Fact]
    public void Undo_leaves_alone_a_setting_changed_by_someone_else()
    {
        var setting = new FakeSetting("power.test", "a");
        var engine = new ChangeEngine(_journal, [setting]);
        var entry = engine.Apply("finding", "power.test", "machine", "b")!;
        setting.Value = "c"; // the user changed it in Windows Settings

        var result = engine.Undo(entry.Id);

        Assert.Equal(JournalStatus.Superseded, result.Status);
        Assert.Equal("c", setting.Value);
    }

    [Fact]
    public void Undo_refuses_an_older_change_while_a_newer_one_on_the_same_setting_is_active()
    {
        var setting = new FakeSetting("power.test", "a");
        var engine = new ChangeEngine(_journal, [setting]);
        var first = engine.Apply("finding", "power.test", "machine", "b")!;
        engine.Apply("finding", "power.test", "machine", "c");

        var result = engine.Undo(first.Id);

        Assert.Equal(JournalStatus.Applied, result.Status);
        Assert.Equal("c", setting.Value);
    }

    [Fact]
    public void UndoAll_goes_back_to_the_original_value()
    {
        var setting = new FakeSetting("power.test", "a");
        var engine = new ChangeEngine(_journal, [setting]);
        engine.Apply("finding", "power.test", "machine", "b");
        engine.Apply("finding", "power.test", "machine", "c");

        var results = engine.UndoAll();

        Assert.All(results, r => Assert.Equal(JournalStatus.Undone, r.Status));
        Assert.Equal("a", setting.Value);
        Assert.Empty(_journal.List(activeOnly: true));
    }

    [Fact]
    public void UndoAll_skips_settings_this_engine_does_not_own()
    {
        var user = new FakeSetting("display.test", "x", ChangeScope.User);
        new ChangeEngine(_journal, [user]).Apply("finding", "display.test", "monitor", "y", "S-1-5-21-1");
        var systemEngine = new ChangeEngine(_journal, [new FakeSetting("power.test", "a")]);

        Assert.Empty(systemEngine.UndoAll());
        Assert.Equal("y", user.Value);
    }
}

public class ChangeEngineJournalFailureTests
{
    private sealed class BrokenJournal : IChangeJournal
    {
        public long Record(NewChange change) => throw new IOException("service went away");
        public void Close(long id, JournalStatus status, string? note = null) { }
        public JournalEntry? Get(long id) => null;
        public IReadOnlyList<JournalEntry> List(bool activeOnly = false) => [];
        public JournalEntry? NewestActive(string settingKey, string target, string? userSid) => null;
    }

    [Fact]
    public void A_change_that_cannot_be_journaled_is_rolled_back()
    {
        var setting = new FakeSetting("display.test", "60", ChangeScope.User);
        var engine = new ChangeEngine(new BrokenJournal(), [setting]);

        Assert.Throws<IOException>(() => engine.Apply("finding", "display.test", "monitor", "165", "S-1-5-21-1"));
        Assert.Equal("60", setting.Value);
    }
}
