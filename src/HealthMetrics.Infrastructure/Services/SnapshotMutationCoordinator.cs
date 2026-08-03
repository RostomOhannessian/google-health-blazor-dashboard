namespace HealthMetrics.Infrastructure.Services;

internal static class SnapshotMutationCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            Gate.Release();
        }
    }
}
