using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateManualLoadAndAzm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                SET
                    ActiveZoneMinutes = CardioLoad,
                    ActiveZoneMinutesAcwr = Acwr,
                    CardioLoad = NULL,
                    TargetLoadMin = NULL,
                    TargetLoadMax = NULL,
                    Acwr = NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE daily_metric_snapshots
                SET
                    CardioLoad = COALESCE(CardioLoad, ActiveZoneMinutes),
                    Acwr = COALESCE(Acwr, ActiveZoneMinutesAcwr);
                """);

            migrationBuilder.DropColumn(
                name: "ActiveZoneMinutes",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "ActiveZoneMinutesAcwr",
                table: "daily_metric_snapshots");
        }
    }
}
