using System.Collections.Concurrent;
using HealthMetrics.Infrastructure.Services;

namespace HealthMetrics.Tests.Services;

public sealed class SnapshotMutationCoordinatorTests
{
    [Fact]
    public async Task RunAsync_SerializesConcurrentMutations()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionOrder = new ConcurrentQueue<string>();

        var firstTask = SnapshotMutationCoordinator.RunAsync(async () =>
        {
            executionOrder.Enqueue("first-start");
            firstEntered.SetResult();
            await allowFirstToFinish.Task;
            executionOrder.Enqueue("first-end");
        });

        await firstEntered.Task;

        var secondTask = SnapshotMutationCoordinator.RunAsync(() =>
        {
            executionOrder.Enqueue("second-start");
            secondEntered.SetResult();
            return Task.CompletedTask;
        });

        Assert.False(secondEntered.Task.IsCompleted);

        allowFirstToFinish.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        Assert.True(secondEntered.Task.IsCompleted);
        Assert.Equal(["first-start", "first-end", "second-start"], executionOrder);
    }
}
