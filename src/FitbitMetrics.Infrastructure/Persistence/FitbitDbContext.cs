using FitbitMetrics.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace FitbitMetrics.Infrastructure.Persistence;

public sealed class FitbitDbContext(DbContextOptions<FitbitDbContext> options) : DbContext(options)
{
    public DbSet<FitbitConnection> FitbitConnections => Set<FitbitConnection>();

    public DbSet<DailyMetricSnapshot> DailyMetricSnapshots => Set<DailyMetricSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FitbitConnection>(entity =>
        {
            entity.ToTable("fitbit_connections");
            entity.HasKey(connection => connection.Id);
            entity.HasIndex(connection => connection.UserKey).IsUnique();
            entity.Property(connection => connection.UserKey).HasMaxLength(120).IsRequired();
            entity.Property(connection => connection.FitbitUserId).HasMaxLength(120).IsRequired();
            entity.Property(connection => connection.AccessToken).HasMaxLength(3000).IsRequired();
            entity.Property(connection => connection.RefreshToken).HasMaxLength(3000).IsRequired();
            entity.Property(connection => connection.Scope).HasMaxLength(800).IsRequired();
        });

        modelBuilder.Entity<DailyMetricSnapshot>(entity =>
        {
            entity.ToTable("daily_metric_snapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.HasIndex(snapshot => new { snapshot.UserKey, snapshot.MetricDate }).IsUnique();
            entity.Property(snapshot => snapshot.UserKey).HasMaxLength(120).IsRequired();
            entity.Property(snapshot => snapshot.HrvRmssdMilliseconds).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.Vo2MaxMlKgMin).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.CarbohydratesGrams).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.FatGrams).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.ProteinGrams).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.FiberGrams).HasPrecision(9, 2);
            entity.Property(snapshot => snapshot.SodiumMilligrams).HasPrecision(12, 2);
            entity.Property(snapshot => snapshot.PotassiumMilligrams).HasPrecision(12, 2);
            entity.Property(snapshot => snapshot.CalciumMilligrams).HasPrecision(12, 2);
            entity.Property(snapshot => snapshot.IronMilligrams).HasPrecision(12, 2);
        });
    }
}
