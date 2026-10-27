using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Zenith.Core.Ipc;

/// <summary>
/// The local channel between the user-session agent (probe now, app later) and the Windows service:
/// one JSON object per line over a named pipe. The service only accepts named commands with fixed
/// effects, never arbitrary setting values for system settings.
/// </summary>
public static class Protocol
{
    public const string PipeName = "Zenith.Core.v1";
    public const int MaxMessageBytes = 1024 * 1024;

    public static class Commands
    {
        public const string Ping = "ping";
        public const string JournalList = "journal.list";
        public const string JournalGet = "journal.get";
        public const string JournalNewestActive = "journal.newest-active";
        public const string PowerFix = "power.fix";
        public const string SystemUndo = "system.undo";
        public const string SystemUndoAll = "system.undo-all";
        public const string UserRecord = "user.record";
        public const string UserClose = "user.close";
        public const string SensorsLatest = "sensors.latest";
        public const string StartupDisable = "startup.disable";
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        await stream.WriteAsync(bytes, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one line, or null at end of stream. Refuses lines over <see cref="MaxMessageBytes"/>.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct = default) where T : class
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, ct);
            if (read == 0) return buffer.Length == 0 ? null : throw new EndOfStreamException("Mensagem incompleta.");
            if (one[0] == (byte)'\n') break;
            if (buffer.Length >= MaxMessageBytes) throw new InvalidDataException("Mensagem grande demais.");
            buffer.WriteByte(one[0]);
        }
        return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(buffer.ToArray()), Json);
    }
}

public sealed record Request(long Id, string Command, JsonObject? Args = null);

public sealed record Response(long Id, bool Ok, JsonNode? Result = null, string? Error = null);

public sealed record UserRecordArgs(string FindingId, string SettingKey, string Target, string PreviousValue, string NewValue);

public sealed record UserCloseArgs(long Id, JournalStatusArg Status, string? Note);

public sealed record NewestActiveArgs(string SettingKey, string Target, string? UserSid);

public sealed record IdArgs(long Id);

/// <summary>A machine-wide startup entry, e.g. "machine-run|Steam".</summary>
public sealed record StartupArgs(string Target);

public sealed record ListArgs(bool ActiveOnly);

public enum JournalStatusArg { Undone, Superseded, UndoFailed }

/// <summary><see cref="CallerSid"/> is who the service thinks is calling; null means changes will be refused.</summary>
public sealed record PingResult(string Version, string JournalPath, string? CallerSid);
