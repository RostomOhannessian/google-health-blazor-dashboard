using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HealthMetrics.Application.Exceptions;
using HealthMetrics.Application.Interfaces;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.DependencyInjection;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Web.Security;
using HealthMetrics.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    ConfigurationSetup.AddLocalConfiguration(builder.Configuration);
    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddMemoryCache();
    builder.Services.AddDataProtection().SetApplicationName("HealthMetrics");
    builder.Services.AddHealthMetricsInfrastructure(builder.Configuration);

    var app = builder.Build();
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HealthMetrics.Startup");
    var endpointLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HealthMetrics.Web.Endpoints");

    startupLogger.LogInformation(
        "Health Metrics starting in {EnvironmentName} with content root {ContentRootPath}.",
        app.Environment.EnvironmentName,
        app.Environment.ContentRootPath);

    try
    {
        startupLogger.LogInformation("Applying Health Metrics database migrations.");
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HealthMetricsDbContext>();
        dbContext.Database.Migrate();
        startupLogger.LogInformation("Health Metrics database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        startupLogger.LogCritical(ex, "Health Metrics database migration failed.");
        throw;
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.Use(async (context, next) =>
    {
        if (LocalRequestPolicy.IsLocal(context))
        {
            await next();
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        startupLogger.LogWarning(
            "Rejected non-local request for {Path} from {RemoteIpAddress}.",
            context.Request.Path,
            remoteIp);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Health Metrics only accepts localhost requests.");
    });

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, _, exception) =>
        {
            if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                return LogEventLevel.Error;

            if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
                return LogEventLevel.Warning;

            var path = httpContext.Request.Path.Value ?? string.Empty;
            return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                ? LogEventLevel.Information
                : LogEventLevel.Debug;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("EndpointName", httpContext.GetEndpoint()?.DisplayName);
        };
    });
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.MapGet("/api/health/status", async (IHealthAuthorizationService authService, CancellationToken cancellationToken) =>
    {
        var status = await authService.GetConnectionStatusAsync(cancellationToken);
        endpointLogger.LogDebug("Google Health connection status requested. Connected: {Connected}.", status.IsConnected);
        return Results.Ok(status);
    });

    app.MapGet(
        "/api/health/connect",
        async (IHealthAuthorizationService authService, IMemoryCache memoryCache, CancellationToken cancellationToken) =>
        {
            endpointLogger.LogInformation("Google Health connect flow started.");
            var state = RandomNumberGenerator.GetHexString(32);
            memoryCache.Set(GetStateCacheKey(state), true, TimeSpan.FromMinutes(10));

            var uri = await authService.BuildAuthorizationUriAsync(state, cancellationToken);
            endpointLogger.LogInformation("Google Health connect flow redirect prepared.");
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
            {
                endpointLogger.LogWarning("Google Health callback rejected because authorization code was missing.");
                return Results.Redirect("/?authError=missing_code");
            }

            if (string.IsNullOrWhiteSpace(state) || !memoryCache.TryGetValue(GetStateCacheKey(state), out bool _))
            {
                endpointLogger.LogWarning("Google Health callback rejected because OAuth state was missing or invalid.");
                return Results.Redirect("/?authError=invalid_state");
            }

            memoryCache.Remove(GetStateCacheKey(state));
            try
            {
                await authService.HandleAuthorizationCodeAsync(code, cancellationToken);
            }
            catch (GoogleAccountSwitchRequiresResetException ex)
            {
                endpointLogger.LogWarning(
                    ex,
                    "Google Health callback rejected because the connection would switch accounts without clearing local history.");
                return Results.Redirect("/?authError=account_switch_blocked");
            }

            endpointLogger.LogInformation("Google Health callback completed successfully.");
            return Results.Redirect("/");
        });

    app.MapGet("/api/metrics", async (
        int? days,
        DateOnly? endDate,
        IMetricQueryService metricQueryService,
        CancellationToken cancellationToken) =>
    {
        var range = TryGetMetricDateRange(days, endDate, 30);
        if (range is null)
        {
            endpointLogger.LogWarning("Metrics query rejected because the requested date range is invalid.");
            return Results.BadRequest("The requested date range must end today or earlier and cannot begin before January 1, 0001.");
        }

        var metrics = await metricQueryService.GetMetricsAsync(
            range.StartDate,
            range.EndDate,
            cancellationToken);
        endpointLogger.LogInformation("Metrics query completed for {RequestedDays} day(s). Returned {MetricCount} row(s).", range.DayCount, metrics.Count);
        return Results.Ok(metrics);
    });

    app.MapGet("/api/metrics/export", async (
        int? days,
        DateOnly? endDate,
        IMetricQueryService metricQueryService,
        CancellationToken cancellationToken) =>
    {
        var range = TryGetMetricDateRange(days, endDate, 366);
        if (range is null)
        {
            endpointLogger.LogWarning("Metrics CSV export rejected because the requested date range is invalid.");
            return Results.BadRequest("The requested date range must end today or earlier and cannot begin before January 1, 0001.");
        }

        var metrics = await metricQueryService.GetMetricsAsync(
            range.StartDate,
            range.EndDate,
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Date,RestingHR_bpm,HRV_RMSSD_ms,DailyVO2Max_ml_kg_min,RunVO2Max_ml_kg_min,ManualCardioLoad,ManualTargetLoad,ManualACWR,SleepEfficiency_pct,DeepSleep_min,RemSleep_min,Calories_kcal,Carbs_g,Fat_g,Protein_g");
        foreach (var m in metrics.OrderBy(m => m.MetricDate))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{m.MetricDate:yyyy-MM-dd},{m.RestingHeartRateBpm},{m.HrvRmssdMilliseconds},{m.DailyVo2MaxMlKgMin},{m.RunVo2MaxMlKgMin},{m.CardioLoad},{m.TargetLoad},{m.Acwr},{m.SleepEfficiency},{m.DeepSleepMinutes},{m.RemSleepMinutes},{m.ConsumedCaloriesKcal},{m.CarbohydratesGrams},{m.FatGrams},{m.ProteinGrams}"));
        }

        var filename = $"health-metrics-{range.EndDate:yyyy-MM-dd}.csv";
        endpointLogger.LogInformation(
            "Metrics CSV export generated for {RequestedDays} day(s). Exported {MetricCount} row(s) to {FileName}.",
            range.DayCount,
            metrics.Count,
            filename);
        return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Health Metrics terminated unexpectedly.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static string GetStateCacheKey(string state) => $"google-health-oauth-state:{state}";

static MetricDateRange? TryGetMetricDateRange(int? days, DateOnly? endDate, int defaultDayCount)
{
    var requestedDays = days is > 0 and <= 366 ? days.Value : defaultDayCount;
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var rangeEndDate = endDate ?? today;

    if (rangeEndDate > today || rangeEndDate.DayNumber < requestedDays - 1)
        return null;

    return new MetricDateRange(rangeEndDate.AddDays(1 - requestedDays), rangeEndDate);
}

public partial class Program;
