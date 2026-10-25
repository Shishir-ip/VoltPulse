using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

public class SqliteHistoryStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteHistoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"voltpulse-db-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
            }
            catch
            {
                // temp cleanup best effort
            }
        }
    }

    private static SqliteHistoryStore Create(string path)
        => new(path, new Serilog.LoggerConfiguration().CreateLogger());

    private static BatterySample Sample(DateTime utc, double percent = 50, int rate = -4000, string id = "BAT1") =>
        new(utc, id, percent, 11600, rate, rate * 1000.0 / 11600.0, rate, 33.0, rate < 0 ? ChargeState.Discharging : ChargeState.Charging);

    [Fact]
    public void AddAndQuery_RoundTrips()
    {
        using var store = Create(_dbPath);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = Enumerable.Range(0, 100).Select(i => Sample(t0.AddSeconds(i))).ToList();
        store.AddSamples(samples);

        var read = store.Query(t0, t0.AddSeconds(200));
        Assert.Equal(100, read.Count);
        Assert.Equal(50, read[10].Percent);
        Assert.Equal(-4000, read[10].RateMw);
        Assert.Equal(ChargeState.Discharging, read[10].State);

        var onlyFirst = store.Query(t0, t0.AddSeconds(9));
        Assert.Equal(10, onlyFirst.Count);
    }

    [Fact]
    public void Downsample_FoldsOldSamplesIntoHourly()
    {
        using var store = Create(_dbPath);
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent = DateTime.UtcNow - TimeSpan.FromSeconds(30);

        store.AddSamples(Enumerable.Range(0, 60).Select(i => Sample(old.AddSeconds(i * 60))).ToList());
        store.AddSamples([Sample(recent)]);

        store.Downsample(old.AddDays(1));

        var fullRes = store.Query(old, old.AddDays(1));
        Assert.Empty(fullRes);

        var hourly = store.QueryHourly(old, old.AddDays(1));
        Assert.Single(hourly);
        Assert.Equal(50, hourly[0].Percent);
        Assert.True(store.SampleCount == 1);
    }

    [Fact]
    public void PruneOlderThan_RemovesHourly()
    {
        using var store = Create(_dbPath);
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        store.AddSamples(Enumerable.Range(0, 10).Select(i => Sample(old.AddMinutes(i))).ToList());
        store.Downsample(old.AddDays(2));
        Assert.Single(store.QueryHourly(old, old.AddDays(2)));

        store.PruneOlderThan(old.AddDays(3));
        Assert.Empty(store.QueryHourly(old, old.AddDays(3)));
    }

    [Fact]
    public void CorruptDatabase_IsRecreatedNotFatal()
    {
        File.WriteAllText(_dbPath, "this is definitely not a sqlite database");
        using var store = Create(_dbPath);
        store.AddSamples([Sample(DateTime.UtcNow)]);

        Assert.True(store.SampleCount == 1);
        Assert.NotEmpty(store.Query(DateTime.UtcNow - TimeSpan.FromMinutes(1), DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void Sessions_RoundTrip()
    {
        using var store = Create(_dbPath);
        var session = new SessionRecord
        {
            BatteryId = "COMBINED",
            StartUtc = DateTime.UtcNow.AddMinutes(-10),
            EndUtc = DateTime.UtcNow,
            Kind = ChargeState.Charging,
            StartPercent = 40,
            EndPercent = 55,
            AvgCurrentMa = 2000,
            PeakCurrentMa = 2500,
            EnergyMWh = 8000,
            SampleCount = 600,
        };
        store.UpsertSession(session);
        Assert.NotNull(session.Id);

        var read = store.GetSessions();
        var found = Assert.Single(read);
        Assert.Equal(ChargeState.Charging, found.Kind);
        Assert.Equal(55, found.EndPercent);
        Assert.Equal(600, found.SampleCount);

        // update path
        found.EndPercent = 56;
        store.UpsertSession(found);
        Assert.Equal(56, Assert.Single(store.GetSessions()).EndPercent);
    }

    [Fact]
    public void ClearHistory_EmptiesEverything()
    {
        using var store = Create(_dbPath);
        store.AddSamples([Sample(DateTime.UtcNow)]);
        store.ClearHistory();
        Assert.Equal(0, store.SampleCount);
        Assert.Empty(store.GetSessions());
    }
}
