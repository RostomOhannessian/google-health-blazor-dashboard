using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMetrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimatedAlcoholEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedAlcoholGrams",
                table: "daily_metric_snapshots",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE daily_metric_snapshots
                SET EstimatedAlcoholGrams = CASE
                    WHEN (
                        CAST(ConsumedCaloriesKcal AS REAL)
                        - CAST(CarbohydratesGrams AS REAL) * 4
                        - CAST(FatGrams AS REAL) * 9
                        - CAST(ProteinGrams AS REAL) * 4
                    ) >= 70
                    THEN ROUND((
                        CAST(ConsumedCaloriesKcal AS REAL)
                        - CAST(CarbohydratesGrams AS REAL) * 4
                        - CAST(FatGrams AS REAL) * 9
                        - CAST(ProteinGrams AS REAL) * 4
                    ) / 7.0, 2)
                    ELSE 0
                END
                WHERE ConsumedCaloriesKcal IS NOT NULL
                    AND CarbohydratesGrams IS NOT NULL
                    AND FatGrams IS NOT NULL
                    AND ProteinGrams IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedAlcoholGrams",
                table: "daily_metric_snapshots");
        }
    }
}
