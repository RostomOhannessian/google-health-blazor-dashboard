using System.Security.Cryptography;
using System.Text;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Infrastructure.DependencyInjection;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Web.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddHealthMetricsInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HealthMetricsDbContext>();
    dbContext.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/health/status", async (IHealthAuthorizationService authService, CancellationToken cancellationToken) =>
{
    var status = await authService.GetConnectionStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapGet(
    "/api/health/connect",
    async (IHealthAuthorizationService authService, IMemoryCache memoryCache, CancellationToken cancellationToken) =>
    {
        var state = RandomNumberGenerator.GetHexString(32);
        memoryCache.Set(GetStateCacheKey(state), true, TimeSpan.FromMinutes(10));

        var uri = await authService.BuildAuthorizationUriAsync(state, cancellationToken);
        return Results.Redirect(uri.ToString());
    });

app.MapGet(
    "/api/health/callback",
    async (
        string? code,
        string? state,
        IHealthAuthorizationService authService,
        IMemoryCache memoryCache,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(code))
            return Results.Redirect("/?authError=missing_code");

        if (string.IsNullOrWhiteSpace(state) || !memoryCache.TryGetValue(GetStateCacheKey(state), out bool _))
            return Results.Redirect("/?authError=invalid_state");

        memoryCache.Remove(GetStateCacheKey(state));
        await authService.HandleAuthorizationCodeAsync(code, cancellationToken);
        return Results.Redirect("/");
    });

app.MapPost("/api/health/disconnect", async (IHealthAuthorizationService authService, CancellationToken cancellationToken) =>
{
    await authService.DisconnectAsync(cancellationToken);
    return Results.Ok();
}).DisableAntiforgery();

app.MapPost("/api/health/sync", async (int? days, IHealthSyncService syncService, CancellationToken cancellationToken) =>
{
    var requestedDays = days is > 0 and <= 90 ? days.Value : 7;
    var result = await syncService.SyncRecentDaysAsync(requestedDays, cancellationToken);
    return Results.Ok(result);
}).DisableAntiforgery();

app.MapGet("/api/metrics", async (int? days, IMetricQueryService metricQueryService, CancellationToken cancellationToken) =>
{
    var requestedDays = days is > 0 and <= 365 ? days.Value : 30;
    var metrics = await metricQueryService.GetRecentMetricsAsync(requestedDays, cancellationToken);
    return Results.Ok(metrics);
});

app.MapGet("/api/metrics/export", async (int? days, IMetricQueryService metricQueryService, CancellationToken cancellationToken) =>
{
    var requestedDays = days is > 0 and <= 365 ? days.Value : 365;
    var metrics = await metricQueryService.GetRecentMetricsAsync(requestedDays, cancellationToken);

    var sb = new StringBuilder();
    sb.AppendLine("Date,RestingHR_bpm,HRV_RMSSD_ms,RunVO2Max_ml_kg_min,Calories_kcal,Carbs_g,Fat_g,Protein_g");
    foreach (var m in metrics.OrderBy(m => m.MetricDate))
    {
        sb.AppendLine(
            $"{m.MetricDate},{m.RestingHeartRateBpm},{m.HrvRmssdMilliseconds},{m.RunVo2MaxMlKgMin}," +
            $"{m.ConsumedCaloriesKcal},{m.CarbohydratesGrams},{m.FatGrams},{m.ProteinGrams}");
    }

    var filename = $"health-metrics-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.csv";
    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
});

app.MapPost("/api/demo/seed", async (int? days, IDemoSeedService demoSeedService, CancellationToken cancellationToken) =>
{
    var dayCount = days is > 0 and <= 90 ? days.Value : 30;
    var inserted = await demoSeedService.SeedAsync(dayCount, cancellationToken);
    return Results.Ok(new { Inserted = inserted, Requested = dayCount });
}).DisableAntiforgery();

app.Run();

static string GetStateCacheKey(string state) => $"google-health-oauth-state:{state}";

public partial class Program;
