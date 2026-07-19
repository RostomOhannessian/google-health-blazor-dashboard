using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitbitMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_history",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PersistedDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_history_UserKey_StartedAtUtc",
                table: "sync_history",
                columns: new[] { "UserKey", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_history");
        }
    }
}
