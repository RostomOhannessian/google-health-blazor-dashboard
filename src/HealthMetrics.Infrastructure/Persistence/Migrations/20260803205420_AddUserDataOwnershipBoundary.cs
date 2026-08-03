using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDataOwnershipBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_data_ownership",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    GoogleUserId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    GoogleEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_data_ownership", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_data_ownership_UserKey",
                table: "user_data_ownership",
                column: "UserKey",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO user_data_ownership (UserKey, GoogleUserId, GoogleEmail, UpdatedAtUtc)
                SELECT UserKey, GoogleUserId, GoogleEmail, UpdatedAtUtc
                FROM health_connections
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM user_data_ownership
                    WHERE user_data_ownership.UserKey = health_connections.UserKey
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_data_ownership");
        }
    }
}
