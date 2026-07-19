using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_metric_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RestingHeartRateBpm = table.Column<int>(type: "INTEGER", nullable: true),
                    HrvRmssdMilliseconds = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    Vo2MaxMlKgMin = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    ConsumedCaloriesKcal = table.Column<int>(type: "INTEGER", nullable: true),
                    CarbohydratesGrams = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    FatGrams = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    ProteinGrams = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    FiberGrams = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    SodiumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    PotassiumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    CalciumMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    IronMilligrams = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_metric_snapshots", x => x.Id);
                });

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
                name: "IX_daily_metric_snapshots_UserKey_MetricDate",
                table: "daily_metric_snapshots",
                columns: new[] { "UserKey", "MetricDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fitbit_connections_UserKey",
                table: "fitbit_connections",
                column: "UserKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_metric_snapshots");

            migrationBuilder.DropTable(
                name: "fitbit_connections");
        }
    }
}