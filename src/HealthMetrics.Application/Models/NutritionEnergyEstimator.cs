namespace HealthMetrics.Application.Models;

public static class NutritionEnergyEstimator
{
    private const decimal CaloriesPerGramOfCarbohydrates = 4m;
    private const decimal CaloriesPerGramOfProtein = 4m;
    private const decimal CaloriesPerGramOfFat = 9m;
    private const decimal CaloriesPerGramOfAlcohol = 7m;
    private const decimal MinimumEstimatedAlcoholGrams = 10m;

    public static decimal? EstimateAlcoholGrams(
        int? consumedCaloriesKcal,
        decimal? carbohydratesGrams,
        decimal? fatGrams,
        decimal? proteinGrams)
    {
        if (consumedCaloriesKcal is null
            || carbohydratesGrams is null
            || fatGrams is null
            || proteinGrams is null)
        {
            return null;
        }

        var remainingEnergyKcal = consumedCaloriesKcal.Value
            - carbohydratesGrams.Value * CaloriesPerGramOfCarbohydrates
            - fatGrams.Value * CaloriesPerGramOfFat
            - proteinGrams.Value * CaloriesPerGramOfProtein;

        if (remainingEnergyKcal < MinimumEstimatedAlcoholGrams * CaloriesPerGramOfAlcohol)
            return 0m;

        return decimal.Round(
            remainingEnergyKcal / CaloriesPerGramOfAlcohol,
            2,
            MidpointRounding.AwayFromZero);
    }

    public static void UpdateEstimatedAlcoholGrams(DailyMetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        snapshot.EstimatedAlcoholGrams = EstimateAlcoholGrams(
            snapshot.ConsumedCaloriesKcal,
            snapshot.CarbohydratesGrams,
            snapshot.FatGrams,
            snapshot.ProteinGrams);
    }
}
