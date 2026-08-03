using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardioLoadAndSleepMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Acwr",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CardioLoad",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeepSleepMinutes",
                table: "daily_metric_snapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemSleepMinutes",
                table: "daily_metric_snapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SleepEfficiency",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetLoadMax",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetLoadMin",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Acwr",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "CardioLoad",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "DeepSleepMinutes",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "RemSleepMinutes",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "SleepEfficiency",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "TargetLoadMax",
                table: "daily_metric_snapshots");

            migrationBuilder.DropColumn(
                name: "TargetLoadMin",
                table: "daily_metric_snapshots");
        }
    }
}
