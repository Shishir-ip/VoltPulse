using System.Globalization;
using Microsoft.Data.Sqlite;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// SQLite-backed history store. Writes are batched by the caller; the schema uses Unix seconds
/// (UTC) so clock changes and DST never distort history. A corrupt database is backed up and
/// recreated automatically instead of crashing.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    private const int CorruptionDetected = 11;
    private const int NotADatabase = 26;

    private readonly string _dbPath;
    private readonly Serilog.ILogger _logger;
    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public SqliteHistoryStore(string dbPath, Serilog.ILogger logger)
    {
        _dbPath = dbPath;
        _logger = logger.ForContext<SqliteHistoryStore>();
        EnsureOpen();
    }

    public long SampleCount
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    EnsureOpen();
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM samples";
                    return (long)(cmd.ExecuteScalar() ?? 0L);
                }
                catch (SqliteException ex)
                {
                    HandleCorruption(ex);
                    return 0;
                }
            }
        }
    }

    public void AddSamples(IReadOnlyList<BatterySample> samples)
    {
        if (samples.Count == 0) return;
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var tx = _connection!.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO samples (ts, battery_id, percent, voltage_mv, rate_mw, current_ma, power_mw, temp_c, state)
                    VALUES ($ts, $id, $p, $v, $r, $c, $pw, $t, $s)
                    """;
                var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
                var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                var pP = cmd.Parameters.Add("$p", SqliteType.Real);
                var pV = cmd.Parameters.Add("$v", SqliteType.Integer);
                var pR = cmd.Parameters.Add("$r", SqliteType.Integer);
                var pC = cmd.Parameters.Add("$c", SqliteType.Real);
                var pPw = cmd.Parameters.Add("$pw", SqliteType.Real);
                var pT = cmd.Parameters.Add("$t", SqliteType.Real);
                var pS = cmd.Parameters.Add("$s", SqliteType.Integer);

                foreach (var s in samples)
                {
                    pTs.Value = ToUnix(s.TimestampUtc);
                    pId.Value = s.BatteryId;
                    pP.Value = (object?)s.Percent ?? DBNull.Value;
                    pV.Value = (object?)s.VoltageMv ?? DBNull.Value;
                    pR.Value = (object?)s.RateMw ?? DBNull.Value;
                    pC.Value = (object?)s.CurrentMa ?? DBNull.Value;
                    pPw.Value = (object?)s.PowerMw ?? DBNull.Value;
                    pT.Value = (object?)s.TempC ?? DBNull.Value;
                    pS.Value = (int)s.State;
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    public IReadOnlyList<BatterySample> Query(DateTime fromUtc, DateTime toUtc, string? batteryId = null)
    {
        var result = new List<BatterySample>();
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = batteryId is null
                    ? "SELECT ts, battery_id, percent, voltage_mv, rate_mw, current_ma, power_mw, temp_c, state FROM samples WHERE ts >= $a AND ts <= $b ORDER BY ts"
                    : "SELECT ts, battery_id, percent, voltage_mv, rate_mw, current_ma, power_mw, temp_c, state FROM samples WHERE ts >= $a AND ts <= $b AND battery_id = $id ORDER BY ts";
                cmd.Parameters.AddWithValue("$a", ToUnix(fromUtc));
                cmd.Parameters.AddWithValue("$b", ToUnix(toUtc));
                if (batteryId is not null) cmd.Parameters.AddWithValue("$id", batteryId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new BatterySample(
                        FromUnix(reader.GetInt64(0)),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        reader.IsDBNull(7) ? null : reader.GetDouble(7),
                        (ChargeState)reader.GetInt32(8)));
                }
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
        return result;
    }

    /// <summary>Hourly aggregates (min/avg/max) for long ranges; only the avg of each hour is returned as samples.</summary>
    public IReadOnlyList<BatterySample> QueryHourly(DateTime fromUtc, DateTime toUtc, string? batteryId = null)
    {
        var result = new List<BatterySample>();
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = batteryId is null
                    ? "SELECT hour, battery_id, percent_avg, current_avg, power_avg, temp_avg FROM samples_hourly WHERE hour >= $a AND hour <= $b ORDER BY hour"
                    : "SELECT hour, battery_id, percent_avg, current_avg, power_avg, temp_avg FROM samples_hourly WHERE hour >= $a AND hour <= $b AND battery_id = $id ORDER BY hour";
                cmd.Parameters.AddWithValue("$a", ToUnix(fromUtc));
                cmd.Parameters.AddWithValue("$b", ToUnix(toUtc));
                if (batteryId is not null) cmd.Parameters.AddWithValue("$id", batteryId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new BatterySample(
                        FromUnix(reader.GetInt64(0)),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        null,
                        null,
                        reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        ChargeState.Unknown));
                }
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
        return result;
    }

    public void ClearHistory()
    {
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM samples; DELETE FROM samples_hourly; DELETE FROM sessions;";
                cmd.ExecuteNonQuery();
                _logger.Information("History cleared");
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    public void UpsertSession(SessionRecord session)
    {
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                if (session.Id is { } id)
                {
                    cmd.CommandText = """
                        UPDATE sessions SET end_utc = $e, end_percent = $ep, avg_current_ma = $ac, peak_current_ma = $pc,
                            energy_mwh = $en, sample_count = $sc WHERE id = $id
                        """;
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$e", ToUnix(session.EndUtc ?? DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("$ep", (object?)session.EndPercent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ac", (object?)session.AvgCurrentMa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$pc", (object?)session.PeakCurrentMa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$en", (object?)session.EnergyMWh ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$sc", session.SampleCount);
                    _ = cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = """
                        INSERT INTO sessions (battery_id, start_utc, end_utc, kind, start_percent, end_percent, avg_current_ma, peak_current_ma, energy_mwh, sample_count)
                        VALUES ($bid, $st, $e, $k, $sp, $ep, $ac, $pc, $en, $sc);
                        SELECT last_insert_rowid();
                        """;
                    cmd.Parameters.AddWithValue("$bid", session.BatteryId);
                    cmd.Parameters.AddWithValue("$st", ToUnix(session.StartUtc));
                    cmd.Parameters.AddWithValue("$e", ToUnix(session.EndUtc ?? DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("$k", (int)session.Kind);
                    cmd.Parameters.AddWithValue("$sp", (object?)session.StartPercent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ep", (object?)session.EndPercent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ac", (object?)session.AvgCurrentMa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$pc", (object?)session.PeakCurrentMa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$en", (object?)session.EnergyMWh ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$sc", session.SampleCount);
                    session.Id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    public void DeleteImportedSessions()
    {
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM sessions WHERE battery_id = $id";
                cmd.Parameters.AddWithValue("$id", "IMPORTED");
                _ = cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    public IReadOnlyList<SessionRecord> GetSessions(int limit = 200, int offset = 0)
    {
        var result = new List<SessionRecord>();
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    SELECT id, battery_id, start_utc, end_utc, kind, start_percent, end_percent, avg_current_ma, peak_current_ma, energy_mwh, sample_count
                    FROM sessions ORDER BY start_utc DESC LIMIT $l OFFSET $o
                    """;
                cmd.Parameters.AddWithValue("$l", limit);
                cmd.Parameters.AddWithValue("$o", offset);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SessionRecord
                    {
                        Id = reader.GetInt64(0),
                        BatteryId = reader.GetString(1),
                        StartUtc = FromUnix(reader.GetInt64(2)),
                        EndUtc = reader.IsDBNull(3) ? null : FromUnix(reader.GetInt64(3)),
                        Kind = (ChargeState)reader.GetInt32(4),
                        StartPercent = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        EndPercent = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        AvgCurrentMa = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                        PeakCurrentMa = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                        EnergyMWh = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                        SampleCount = reader.GetInt32(10),
                    });
                }
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
        return result;
    }

    public void Downsample(DateTime olderThanUtc)
    {
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                long cutoff = ToUnix(olderThanUtc);
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    INSERT OR REPLACE INTO samples_hourly (hour, battery_id, percent_avg, percent_min, percent_max, current_avg, current_min, current_max, power_avg, temp_avg, n)
                    SELECT (ts / 3600) * 3600, battery_id,
                           AVG(percent), MIN(percent), MAX(percent),
                           AVG(current_ma), MIN(current_ma), MAX(current_ma),
                           AVG(power_mw), AVG(temp_c), COUNT(*)
                    FROM samples WHERE ts < $c GROUP BY (ts / 3600) * 3600, battery_id;
                    DELETE FROM samples WHERE ts < $c;
                    """;
                cmd.Parameters.AddWithValue("$c", cutoff);
                int affected = cmd.ExecuteNonQuery();
                _logger.Debug("Downsampled history older than {Cutoff} ({Affected})", olderThanUtc, affected);
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    public void PruneOlderThan(DateTime cutoffUtc)
    {
        lock (_gate)
        {
            try
            {
                EnsureOpen();
                long cutoff = ToUnix(cutoffUtc);
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM samples_hourly WHERE hour < $c; DELETE FROM sessions WHERE start_utc < $c;";
                cmd.Parameters.AddWithValue("$c", cutoff);
                _ = cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                HandleCorruption(ex);
            }
        }
    }

    private void EnsureOpen()
    {
        if (_connection is not null) return;
        try
        {
            OpenCore();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is CorruptionDetected or NotADatabase)
        {
            RecoverFromCorruption();
            OpenCore();
        }
    }

    private void OpenCore()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Pooling=false");
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        _ = pragma.ExecuteNonQuery();
        CreateSchema();
    }

    private void RecoverFromCorruption()
    {
        _logger.Error("History database corrupted; recreating");
        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // already broken
        }
        _connection = null;
        try
        {
            string backup = $"{_dbPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (File.Exists(_dbPath)) File.Move(_dbPath, backup);
            string wal = _dbPath + "-wal";
            string shm = _dbPath + "-shm";
            if (File.Exists(wal)) File.Move(wal, wal + ".corrupt");
            if (File.Exists(shm)) File.Move(shm, shm + ".corrupt");
        }
        catch (Exception moveEx)
        {
            _logger.Error(moveEx, "Failed to move the corrupt database aside");
        }
    }

    private void CreateSchema()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS samples (
                ts INTEGER NOT NULL,
                battery_id TEXT NOT NULL,
                percent REAL, voltage_mv INTEGER, rate_mw INTEGER,
                current_ma REAL, power_mw REAL, temp_c REAL,
                state INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_samples_ts ON samples(ts);
            CREATE TABLE IF NOT EXISTS samples_hourly (
                hour INTEGER NOT NULL,
                battery_id TEXT NOT NULL,
                percent_avg REAL, percent_min REAL, percent_max REAL,
                current_avg REAL, current_min REAL, current_max REAL,
                power_avg REAL, temp_avg REAL, n INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (hour, battery_id));
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                battery_id TEXT NOT NULL,
                start_utc INTEGER NOT NULL, end_utc INTEGER,
                kind INTEGER NOT NULL,
                start_percent REAL, end_percent REAL,
                avg_current_ma REAL, peak_current_ma REAL, energy_mwh REAL,
                sample_count INTEGER NOT NULL DEFAULT 0);
            """;
        cmd.ExecuteNonQuery();
    }

    private void HandleCorruption(SqliteException ex)
    {
        if (ex.SqliteErrorCode is not (CorruptionDetected or NotADatabase))
        {
            _logger.Warning(ex, "History database operation failed ({Code})", ex.SqliteErrorCode);
            return;
        }

        // Unrecoverable database mid-operation: keep the broken file for inspection, start fresh.
        RecoverFromCorruption();
        try
        {
            EnsureOpen();
        }
        catch (Exception reopenEx)
        {
            _logger.Error(reopenEx, "Failed to reopen the history database after corruption");
        }
    }

    private static long ToUnix(DateTime utc) => ((DateTimeOffset)DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

    public void Dispose()
    {
        lock (_gate)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
