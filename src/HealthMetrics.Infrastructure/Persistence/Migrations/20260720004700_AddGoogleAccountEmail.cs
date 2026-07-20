using HealthMetrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HealthMetricsDbContext))]
[Migration("20260720004700_AddGoogleAccountEmail")]
public partial class AddGoogleAccountEmail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GoogleEmail",
            table: "health_connections",
            type: "TEXT",
            maxLength: 320,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GoogleEmail",
            table: "health_connections");
    }
}
