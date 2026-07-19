using FitbitMetrics.Application.Interfaces;
using FitbitMetrics.Application.Models;
using FitbitMetrics.Infrastructure.DependencyInjection;
using FitbitMetrics.Infrastructure.Persistence;
using FitbitMetrics.Web.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddFitbitMetricsInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FitbitDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/api/fitbit/status", async (IFitbitOAuthService authService, CancellationToken cancellationToken) =>
{
    var status = await authService.GetConnectionStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapGet(
    "/api/fitbit/connect",
    async (IFitbitOAuthService authService, IMemoryCache memoryCache, CancellationToken cancellationToken) =>
    {
        var state = Guid.NewGuid().ToString("N");
        memoryCache.Set(GetStateCacheKey(state), true, TimeSpan.FromMinutes(10));

        var uri = await authService.BuildAuthorizationUriAsync(state, cancellationToken);
        return Results.Redirect(uri.ToString());
    });

app.MapGet(
    "/api/fitbit/callback",
    async (
        string? code,
        string? state,
        IFitbitOAuthService authService,
        IMemoryCache memoryCache,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.Redirect("/?authError=missing_code");
        }

        if (string.IsNullOrWhiteSpace(state)
            || !memoryCache.TryGetValue(GetStateCacheKey(state), out bool _))
        {
            return Results.Redirect("/?authError=invalid_state");
        }

        memoryCache.Remove(GetStateCacheKey(state));
        await authService.HandleAuthorizationCodeAsync(code, cancellationToken);
        return Results.Redirect("/");
    });

app.MapPost("/api/fitbit/disconnect", async (IFitbitOAuthService authService, CancellationToken cancellationToken) =>
{
    await authService.DisconnectAsync(cancellationToken);
    return Results.Ok();
}).DisableAntiforgery();

app.MapPost("/api/fitbit/sync", async (int? days, IFitbitSyncService syncService, CancellationToken cancellationToken) =>
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
    sb.AppendLine("Date,RestingHR_bpm,HRV_RMSSD_ms,VO2Max_ml_kg_min,Calories_kcal,Carbs_g,Fat_g,Protein_g,Fiber_g,Sodium_mg,Potassium_mg,Calcium_mg,Iron_mg");
    foreach (var m in metrics.OrderBy(m => m.MetricDate))
    {
        sb.AppendLine(
            $"{m.MetricDate},{m.RestingHeartRateBpm},{m.HrvRmssdMilliseconds},{m.Vo2MaxMlKgMin}," +
            $"{m.ConsumedCaloriesKcal},{m.CarbohydratesGrams},{m.FatGrams},{m.ProteinGrams}," +
            $"{m.FiberGrams},{m.SodiumMilligrams},{m.PotassiumMilligrams},{m.CalciumMilligrams},{m.IronMilligrams}");
    }

    var filename = $"fitbit-metrics-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.csv";
    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
});

app.MapPost("/api/demo/seed", async (int? days, IDemoSeedService demoSeedService, CancellationToken cancellationToken) =>
{
    var dayCount = days is > 0 and <= 90 ? days.Value : 30;
    var inserted = await demoSeedService.SeedAsync(dayCount, cancellationToken);
    return Results.Ok(new { Inserted = inserted, Requested = dayCount });
}).DisableAntiforgery();

app.Run();

static string GetStateCacheKey(string state) => $"fitbit-oauth-state:{state}";
