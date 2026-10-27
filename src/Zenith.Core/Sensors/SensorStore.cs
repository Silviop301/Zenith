using Microsoft.Data.Sqlite;

namespace Zenith.Core.Sensors;

/// <summary>One reading. <see cref="Target"/> names what was measured ("NVIDIA GeForce RTX 3060", "disco 0"), or "" for the whole PC.</summary>
public sealed record SensorSample(string Sensor, string Target, double Value, DateTimeOffset At);

/// <summary>
/// Sensor history for the future Revisão module, kept only on this PC (%ProgramData%\Zenith\sensors.db).
/// Also remembers when each source last ran, so daily sources stay daily across restarts.
/// </summary>
public sealed class SensorStore : IDisposable
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    private SensorStore(SqliteConnection connection) => _connection = connection;

    public static SensorStore Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS samples (
                at      INTEGER NOT NULL,
                sensor  TEXT NOT NULL,
                target  TEXT NOT NULL,
                value   REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS samples_at ON samples (at);
            CREATE INDEX IF NOT EXISTS samples_sensor ON samples (sensor, target, at);
            CREATE TABLE IF NOT EXISTS source_runs (
                source  TEXT PRIMARY KEY,
                at      INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        return new SensorStore(connection);
    }

    public void Add(IEnumerable<SensorSample> samples)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO samples (at, sensor, target, value) VALUES ($at, $sensor, $target, $value);";
            var at = command.Parameters.Add("$at", SqliteType.Integer);
            var sensor = command.Parameters.Add("$sensor", SqliteType.Text);
            var target = command.Parameters.Add("$target", SqliteType.Text);
            var value = command.Parameters.Add("$value", SqliteType.Real);
            foreach (var s in samples)
            {
                at.Value = s.At.ToUnixTimeSeconds();
                sensor.Value = s.Sensor;
                target.Value = s.Target;
                value.Value = s.Value;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    /// <summary>The newest reading of each sensor and target.</summary>
    public IReadOnlyList<SensorSample> Latest()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT s.sensor, s.target, s.value, s.at FROM samples s
                JOIN (SELECT sensor, target, MAX(at) AS at FROM samples GROUP BY sensor, target) n
                  ON s.sensor = n.sensor AND s.target = n.target AND s.at = n.at
                GROUP BY s.sensor, s.target
                ORDER BY s.sensor, s.target;
                """;
            using var reader = command.ExecuteReader();
            var result = new List<SensorSample>();
            while (reader.Read())
                result.Add(new SensorSample(reader.GetString(0), reader.GetString(1), reader.GetDouble(2),
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)).ToLocalTime()));
            return result;
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM samples;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>Deletes readings older than <see cref="Retention"/>. Returns how many went.</summary>
    public int Prune(DateTimeOffset now)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM samples WHERE at < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", (now - Retention).ToUnixTimeSeconds());
            return command.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<string, DateTimeOffset> LastRuns()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT source, at FROM source_runs;";
            using var reader = command.ExecuteReader();
            var result = new Dictionary<string, DateTimeOffset>();
            while (reader.Read()) result[reader.GetString(0)] = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));
            return result;
        }
    }

    public void MarkRun(string source, DateTimeOffset at)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO source_runs (source, at) VALUES ($source, $at) ON CONFLICT(source) DO UPDATE SET at = $at;";
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$at", at.ToUnixTimeSeconds());
            command.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();
}
