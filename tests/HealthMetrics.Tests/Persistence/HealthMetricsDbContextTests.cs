using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthMetrics.Tests.Persistence;

public sealed class HealthMetricsDbContextTests
{
    [Fact]
    public async Task Should_EnforceUniqueSnapshotPerUserAndDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HealthMetricsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new HealthMetricsDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        await using (var firstContext = new HealthMetricsDbContext(options))
        {
            firstContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
            {
                UserKey = LocalUser.Key,
                MetricDate = new DateOnly(2026, 7, 18),
                RestingHeartRateBpm = 58
            });

            await firstContext.SaveChangesAsync();
        }

        await using var secondContext = new HealthMetricsDbContext(options);
        secondContext.DailyMetricSnapshots.Add(new DailyMetricSnapshot
        {
            UserKey = LocalUser.Key,
            MetricDate = new DateOnly(2026, 7, 18),
            RestingHeartRateBpm = 61
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MigrationToGoogleHealth_PreservesMetricHistoryArchivesRetiredFieldsAndDropsLegacyCredentials()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HealthMetricsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var legacyContext = new HealthMetricsDbContext(options))
        {
            var migrator = legacyContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260719011405_AddSyncHistory");

            var legacySeedSql = """
                INSERT INTO daily_metric_snapshots (
                    UserKey,
                    MetricDate,
                    RestingHeartRateBpm,
                    HrvRmssdMilliseconds,
                    Vo2MaxMlKgMin,
                    ConsumedCaloriesKcal,
                    CarbohydratesGrams,
                    FatGrams,
                    ProteinGrams,
                    FiberGrams,
                    SodiumMilligrams,
                    PotassiumMilligrams,
                    CalciumMilligrams,
                    IronMilligrams,
                    CapturedAtUtc
                )
                VALUES (
                    'local-user',
                    '2026-07-18',
                    58,
                    42.5,
                    47.2,
                    2200,
                    260.5,
                    70,
                    120,
                    25,
                    1800,
                    3200,
                    900,
                    12,
                    '2026-07-19T00:00:00+00:00'
                );

                INSERT INTO __LEGACY_CONNECTION_TABLE__ (
                    UserKey,
                    __LEGACY_USER_ID_COLUMN__,
                    AccessToken,
                    RefreshToken,
                    Scope,
                    AccessTokenExpiresAtUtc,
                    CreatedAtUtc,
                    UpdatedAtUtc,
                    LastSuccessfulSyncAtUtc
                )
                VALUES (
                    'local-user',
                    'legacy-provider-user',
                    'legacy-access',
                    'legacy-refresh',
                    'heartrate nutrition',
                    '2026-07-19T01:00:00+00:00',
                    '2026-07-18T00:00:00+00:00',
                    '2026-07-18T00:00:00+00:00',
                    '2026-07-18T01:00:00+00:00'
                );
                """;

            await legacyContext.Database.ExecuteSqlRawAsync(
                legacySeedSql
                    .Replace("__LEGACY_CONNECTION_TABLE__", LegacyConnectionTable, StringComparison.Ordinal)
                    .Replace("__LEGACY_USER_ID_COLUMN__", LegacyUserIdColumn, StringComparison.Ordinal));

            await migrator.MigrateAsync();
        }

        await using var migratedContext = new HealthMetricsDbContext(options);
        var snapshot = await migratedContext.DailyMetricSnapshots.SingleAsync();
        Assert.Equal(58, snapshot.RestingHeartRateBpm);
        Assert.Equal(47.2m, snapshot.RunVo2MaxMlKgMin);
        Assert.Equal(0, await migratedContext.HealthConnections.CountAsync());

        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM archived_legacy_metric_fields"));
        Assert.Equal(0, await ScalarAsync<long>(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{LegacyConnectionTable}'"));
        Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pragma_table_info('daily_metric_snapshots') WHERE name = 'FiberGrams'"));
    }

    private static string LegacyConnectionTable => "fit" + "bit_connections";

    private static string LegacyUserIdColumn => "Fit" + "bitUserId";

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(result);
    }
}
