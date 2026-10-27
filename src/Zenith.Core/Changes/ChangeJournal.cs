using Microsoft.Data.Sqlite;

namespace Zenith.Core.Changes;

/// <summary>Who owns a setting: the whole machine (changed by the service) or one signed-in user.</summary>
public enum ChangeScope { System, User }

public enum JournalStatus
{
    /// <summary>The change is in effect.</summary>
    Applied,
    /// <summary>The previous value was restored.</summary>
    Undone,
    /// <summary>Someone else changed the setting after Zenith did, so undo left it alone.</summary>
    Superseded,
    /// <summary>Restoring the previous value failed; see the note.</summary>
    UndoFailed,
}

public sealed record JournalEntry(
    long Id,
    string FindingId,
    string SettingKey,
    ChangeScope Scope,
    string? UserSid,
    string Target,
    string PreviousValue,
    string NewValue,
    DateTimeOffset AppliedAt,
    JournalStatus Status,
    DateTimeOffset? ClosedAt,
    string? Note);

public sealed record NewChange(
    string FindingId, string SettingKey, ChangeScope Scope, string? UserSid, string Target, string PreviousValue, string NewValue);

public interface IChangeJournal
{
    long Record(NewChange change);
    void Close(long id, JournalStatus status, string? note = null);
    JournalEntry? Get(long id);
    IReadOnlyList<JournalEntry> List(bool activeOnly = false);
    JournalEntry? NewestActive(string settingKey, string target, string? userSid);
}

/// <summary>
/// Every change Zenith makes, with the value before and after, so each one can be undone.
/// Entries are never deleted: undoing a change closes its entry with a status.
/// Owned by the Windows service; the user-session agent reaches it through <see cref="Ipc.RemoteJournal"/>.
/// </summary>
public sealed class ChangeJournal : IChangeJournal, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    private ChangeJournal(SqliteConnection connection) => _connection = connection;

    public static ChangeJournal Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS changes (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                finding_id     TEXT NOT NULL,
                setting_key    TEXT NOT NULL,
                scope          TEXT NOT NULL,
                user_sid       TEXT,
                target         TEXT NOT NULL,
                previous_value TEXT NOT NULL,
                new_value      TEXT NOT NULL,
                applied_at     TEXT NOT NULL,
                status         TEXT NOT NULL,
                closed_at      TEXT,
                note           TEXT
            );
            """;
        command.ExecuteNonQuery();
        return new ChangeJournal(connection);
    }

    public long Record(NewChange change)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO changes (finding_id, setting_key, scope, user_sid, target, previous_value, new_value, applied_at, status)
                VALUES ($finding, $key, $scope, $sid, $target, $prev, $new, $at, $status);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$finding", change.FindingId);
            command.Parameters.AddWithValue("$key", change.SettingKey);
            command.Parameters.AddWithValue("$scope", change.Scope.ToString());
            command.Parameters.AddWithValue("$sid", (object?)change.UserSid ?? DBNull.Value);
            command.Parameters.AddWithValue("$target", change.Target);
            command.Parameters.AddWithValue("$prev", change.PreviousValue);
            command.Parameters.AddWithValue("$new", change.NewValue);
            command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
            command.Parameters.AddWithValue("$status", JournalStatus.Applied.ToString());
            return (long)command.ExecuteScalar()!;
        }
    }

    public void Close(long id, JournalStatus status, string? note = null)
    {
        if (status == JournalStatus.Applied) throw new ArgumentException("Closing an entry needs a final status.", nameof(status));
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE changes SET status = $status, closed_at = $at, note = $note WHERE id = $id AND status = 'Applied';";
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
            command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", id);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"A alteração {id} não existe ou já foi desfeita.");
        }
    }

    public JournalEntry? Get(long id) => Query("WHERE id = $id", ("$id", id)).FirstOrDefault();

    /// <summary>Newest first. <paramref name="activeOnly"/> keeps only changes still in effect.</summary>
    public IReadOnlyList<JournalEntry> List(bool activeOnly = false) =>
        Query(activeOnly ? "WHERE status = 'Applied'" : "");

    /// <summary>The most recent change still in effect on one setting, if any.</summary>
    public JournalEntry? NewestActive(string settingKey, string target, string? userSid) =>
        Query("WHERE status = 'Applied' AND setting_key = $key AND target = $target AND user_sid IS $sid",
            ("$key", settingKey), ("$target", target), ("$sid", (object?)userSid ?? DBNull.Value)).FirstOrDefault();

    private List<JournalEntry> Query(string where, params (string Name, object Value)[] parameters)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT id, finding_id, setting_key, scope, user_sid, target, previous_value, new_value, applied_at, status, closed_at, note
                FROM changes {where}
                ORDER BY id DESC
                """;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

            using var reader = command.ExecuteReader();
            var result = new List<JournalEntry>();
            while (reader.Read())
            {
                result.Add(new JournalEntry(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), Enum.Parse<ChangeScope>(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    DateTimeOffset.Parse(reader.GetString(8)), Enum.Parse<JournalStatus>(reader.GetString(9)),
                    reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            }
            return result;
        }
    }

    public void Dispose() => _connection.Dispose();
}
