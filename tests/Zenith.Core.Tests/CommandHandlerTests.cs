using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.Core.Changes;
using Zenith.Core.Ipc;
using Zenith.Core.Settings;

namespace Zenith.Core.Tests;

public sealed class CommandHandlerTests : IDisposable
{
    private const string Alice = "S-1-5-21-100";
    private const string Bob = "S-1-5-21-200";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenith-test-{Guid.NewGuid():N}.db");
    private readonly ChangeJournal _journal;
    private readonly FakeSetting _power = new("power.test", "a");
    private readonly CommandHandler _handler;

    public CommandHandlerTests()
    {
        _journal = ChangeJournal.Open(_path);
        var engine = new ChangeEngine(_journal, [_power]);
        _handler = new CommandHandler(_journal, engine,
            () => [engine.Apply("power-plan", "power.test", "machine", "b")!], _path);
    }

    public void Dispose()
    {
        _journal.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    private Response Send(string command, object? args, string? sid) =>
        _handler.Handle(new Request(1, command, args is null ? null : JsonSerializer.SerializeToNode(args, Protocol.Json)!.AsObject()), sid);

    private long RecordAs(string sid) =>
        Send(Protocol.Commands.UserRecord,
            new UserRecordArgs("hdr-off", HdrSetting.SettingKey, @"\\?\DISPLAY#GSM", "off", "on"), sid).Result!.GetValue<long>();

    [Fact]
    public void User_record_stamps_the_callers_identity()
    {
        var id = RecordAs(Alice);

        var entry = _journal.Get(id)!;
        Assert.Equal((ChangeScope.User, Alice), (entry.Scope, entry.UserSid));
    }

    [Fact]
    public void User_record_without_identity_is_refused()
    {
        var response = Send(Protocol.Commands.UserRecord,
            new UserRecordArgs("hdr-off", HdrSetting.SettingKey, "monitor", "off", "on"), sid: null);

        Assert.False(response.Ok);
        Assert.Empty(_journal.List());
    }

    [Fact]
    public void User_record_of_an_unknown_setting_is_refused()
    {
        var response = Send(Protocol.Commands.UserRecord, new UserRecordArgs("x", "power.active-scheme", "machine", "a", "b"), Alice);

        Assert.False(response.Ok);
    }

    [Fact]
    public void Only_the_owner_can_close_a_user_change()
    {
        var id = RecordAs(Alice);

        var asBob = Send(Protocol.Commands.UserClose, new UserCloseArgs(id, JournalStatusArg.Undone, null), Bob);
        var asAlice = Send(Protocol.Commands.UserClose, new UserCloseArgs(id, JournalStatusArg.Undone, null), Alice);

        Assert.False(asBob.Ok);
        Assert.True(asAlice.Ok);
        Assert.Equal(JournalStatus.Undone, _journal.Get(id)!.Status);
    }

    [Fact]
    public void Service_will_not_undo_a_user_change()
    {
        var id = RecordAs(Alice);

        var response = Send(Protocol.Commands.SystemUndo, new IdArgs(id), Alice);

        Assert.False(response.Ok);
        Assert.Equal(JournalStatus.Applied, _journal.Get(id)!.Status);
    }

    [Fact]
    public void Power_fix_then_undo_all_restores_the_machine_setting()
    {
        Assert.True(Send(Protocol.Commands.PowerFix, null, Alice).Ok);
        Assert.Equal("b", _power.Value);

        Assert.True(Send(Protocol.Commands.SystemUndoAll, null, Alice).Ok);
        Assert.Equal("a", _power.Value);
    }

    [Fact]
    public void Unknown_command_is_an_error_not_a_crash()
    {
        var response = Send("format-c", null, Alice);

        Assert.False(response.Ok);
    }

    [Fact]
    public async Task Messages_survive_the_line_protocol()
    {
        var stream = new MemoryStream();
        await Protocol.WriteAsync(stream, new Request(7, Protocol.Commands.Ping, new JsonObject { ["x"] = "ação" }), TestContext.Current.CancellationToken);
        stream.Position = 0;

        var read = await Protocol.ReadAsync<Request>(stream, TestContext.Current.CancellationToken);

        Assert.Equal(7, read!.Id);
        Assert.Equal("ação", read.Args!["x"]!.GetValue<string>());
    }
}

public class GpuPreferenceValueTests
{
    [Theory]
    [InlineData(null, "2", "GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=1;", "2", "SwapEffectUpgradeEnable=1;GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=2;", "unset", "SwapEffectUpgradeEnable=1;")]
    [InlineData("GpuPreference=2;", "unset", null)]
    public void Changes_only_the_gpu_preference_field(string? existing, string value, string? expected) =>
        Assert.Equal(expected, GpuPreferenceSetting.WithPreference(existing, value));
}
