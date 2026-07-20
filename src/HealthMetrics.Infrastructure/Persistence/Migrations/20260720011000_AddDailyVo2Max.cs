using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HealthMetricsDbContext))]
[Migration("20260720011000_AddDailyVo2Max")]
public partial class AddDailyVo2Max : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "DailyVo2MaxMlKgMin",
            table: "daily_metric_snapshots",
            type: "TEXT",
            precision: 9,
            scale: 2,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DailyVo2MaxMlKgMin",
            table: "daily_metric_snapshots");
    }
}
