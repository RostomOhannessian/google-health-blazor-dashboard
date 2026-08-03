using HealthMetrics.Application.Models;

namespace HealthMetrics.Application.Interfaces;

public interface IManualLoadEntryService
{
    Task<ManualLoadEntryResult> SaveAsync(
        ManualLoadEntry entry,
        CancellationToken cancellationToken = default);
}
