using HealthMetrics.Application.Models;

namespace HealthMetrics.Tests.Models;

public sealed class NutritionEnergyEstimatorTests
{
    [Fact]
    public void EstimateAlcoholGrams_WhenResidualIsSubstantial_ReturnsRemainingEnergyInGrams()
    {
        var estimate = NutritionEnergyEstimator.EstimateAlcoholGrams(
            consumedCaloriesKcal: 2300,
            carbohydratesGrams: 260.5m,
            fatGrams: 70m,
            proteinGrams: 120m);

        Assert.Equal(21.14m, estimate);
    }

    [Fact]
    public void EstimateAlcoholGrams_WhenResidualIsBelowThreshold_ReturnsZero()
    {
        var estimate = NutritionEnergyEstimator.EstimateAlcoholGrams(
            consumedCaloriesKcal: 2200,
            carbohydratesGrams: 260.5m,
            fatGrams: 70m,
            proteinGrams: 120m);

        Assert.Equal(0m, estimate);
    }

    [Fact]
    public void EstimateAlcoholGrams_WhenNutritionIsIncomplete_ReturnsNull()
    {
        var estimate = NutritionEnergyEstimator.EstimateAlcoholGrams(
            consumedCaloriesKcal: 2300,
            carbohydratesGrams: 260.5m,
            fatGrams: null,
            proteinGrams: 120m);

        Assert.Null(estimate);
    }
}
