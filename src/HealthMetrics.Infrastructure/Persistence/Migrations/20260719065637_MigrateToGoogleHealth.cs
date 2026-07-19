using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateToGoogleHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archived_legacy_metric_fields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FiberGrams = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    SodiumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    PotassiumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    CalciumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    IronMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_legacy_metric_fields", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO archived_legacy_metric_fields (
                    SnapshotId,
                    UserKey,
                    MetricDate,
                    FiberGrams,
                    SodiumMilligrams,
                    PotassiumMilligrams,
                    CalciumMilligrams,
                    IronMilligrams,
                    ArchivedAtUtc
                )
                SELECT
                    Id,
                    UserKey,
                    MetricDate,
                    FiberGrams,
                    SodiumMilligrams,
                    PotassiumMilligrams,
                    CalciumMilligrams,
                    IronMilligrams,
                    strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now')
                FROM daily_metric_snapshots
                WHERE FiberGrams IS NOT NULL
                   OR SodiumMilligrams IS NOT NULL
                   OR PotassiumMilligrams IS NOT NULL
                   OR CalciumMilligrams IS NOT NULL
                   OR IronMilligrams IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "fitbit_connections");

            migrationBuilder.DropColumn(
                name: "CalciumMilligrams",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "FiberGrams",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "IronMilligrams",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "PotassiumMilligrams",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "SodiumMilligrams",
                table: "daily_metric_snapshots");

            migrationBuilder.RenameColumn(
                name: "Vo2MaxMlKgMin",
                table: "daily_metric_snapshots",
                newName: "RunVo2MaxMlKgMin");

            migrationBuilder.CreateTable(
                name: "health_connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    GoogleUserId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RefreshTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_health_connections_UserKey",
                table: "health_connections",
                column: "UserKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "health_connections");

            migrationBuilder.RenameColumn(
                name: "RunVo2MaxMlKgMin",
                table: "daily_metric_snapshots",
                newName: "Vo2MaxMlKgMin");

            migrationBuilder.AddColumn<decimal>(
                name: "CalciumMilligrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiberGrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IronMilligrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PotassiumMilligrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SodiumMilligrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE daily_metric_snapshots
                SET
                    FiberGrams = (
                        SELECT FiberGrams
                        FROM archived_legacy_metric_fields
                        WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                    ),
                    SodiumMilligrams = (
                        SELECT SodiumMilligrams
                        FROM archived_legacy_metric_fields
                        WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                    ),
                    PotassiumMilligrams = (
                        SELECT PotassiumMilligrams
                        FROM archived_legacy_metric_fields
                        WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                    ),
                    CalciumMilligrams = (
                        SELECT CalciumMilligrams
                        FROM archived_legacy_metric_fields
                        WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                    ),
                    IronMilligrams = (
                        SELECT IronMilligrams
                        FROM archived_legacy_metric_fields
                        WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                    )
                WHERE EXISTS (
                    SELECT 1
                    FROM archived_legacy_metric_fields
                    WHERE archived_legacy_metric_fields.SnapshotId = daily_metric_snapshots.Id
                );
                """);

            migrationBuilder.DropTable(
                name: "archived_legacy_metric_fields");

            migrationBuilder.CreateTable(
                name: "fitbit_connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    FitbitUserId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 3000, nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", maxLength: 3000, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 800, nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fitbit_connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fitbit_connections_UserKey",
                table: "fitbit_connections",
                column: "UserKey",
                unique: true);
        }
    }
}
