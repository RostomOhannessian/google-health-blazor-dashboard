using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAzmAndSimplifyManualTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE daily_metric_snapshots
                SET TargetLoadMin = CASE
                    WHEN TargetLoadMin IS NOT NULL AND TargetLoadMax IS NOT NULL
                        THEN ROUND((TargetLoadMin + TargetLoadMax) / 2.0, 2)
                    ELSE COALESCE(TargetLoadMin, TargetLoadMax)
                END;
                """);

            migrationBuilder.DropColumn(
                name: "ActiveZoneMinutes",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "ActiveZoneMinutesAcwr",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "TargetLoadMax",
                table: "daily_metric_snapshots");

            migrationBuilder.RenameColumn(
                name: "TargetLoadMin",
                table: "daily_metric_snapshots",
                newName: "TargetLoad");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TargetLoad",
                table: "daily_metric_snapshots",
                newName: "TargetLoadMin");

            migrationBuilder.AddColumn<decimal>(
                name: "TargetLoadMax",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActiveZoneMinutes",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActiveZoneMinutesAcwr",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE daily_metric_snapshots
                SET TargetLoadMax = TargetLoadMin;
                """);
        }
    }
}
