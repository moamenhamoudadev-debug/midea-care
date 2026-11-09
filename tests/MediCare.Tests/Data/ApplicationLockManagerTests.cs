using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Locking;

public class ApplicationLockManagerTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AcquireLocksAsync_WhenEmptyOrNull_ReturnsDisposableNoOp()
    {
        using var context = CreateInMemoryDbContext();

        var lockHandle1 = await ApplicationLockManager.AcquireLocksAsync(context, Array.Empty<string>(), TimeSpan.FromSeconds(1));
        var lockHandle2 = await ApplicationLockManager.AcquireLocksAsync(context, new List<string>(), TimeSpan.FromSeconds(1));

        lockHandle1.Should().NotBeNull();
        lockHandle2.Should().NotBeNull();

        await lockHandle1.DisposeAsync();
        await lockHandle2.DisposeAsync();
    }

    [Fact]
    public async Task AcquireLocksAsync_DeduplicatesKeysAndSortsDeterministically()
    {
        using var context = CreateInMemoryDbContext();

        // Pass duplicate and out-of-order keys
        var keys = new[] { "Key_Z", "Key_A", "Key_Z", "Key_M" };

        var lockHandle = await ApplicationLockManager.AcquireLocksAsync(context, keys, TimeSpan.FromSeconds(2));
        lockHandle.Should().NotBeNull();

        // While locked, a second acquire for any of those keys from another task with a tiny timeout should time out
        var timeoutTask = Task.Run(async () =>
        {
            try
            {
                using var secondContext = CreateInMemoryDbContext();
                await ApplicationLockManager.AcquireLocksAsync(secondContext, new[] { "Key_A" }, TimeSpan.FromMilliseconds(50));
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        });

        var secondAcquired = await timeoutTask;
        secondAcquired.Should().BeFalse("Key_A is currently held by lockHandle");

        await lockHandle.DisposeAsync();

        // After release, acquiring Key_A should immediately succeed
        using var thirdContext = CreateInMemoryDbContext();
        var afterReleaseHandle = await ApplicationLockManager.AcquireLocksAsync(thirdContext, new[] { "Key_A" }, TimeSpan.FromSeconds(1));
        afterReleaseHandle.Should().NotBeNull();
        await afterReleaseHandle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireLocksAsync_WhenTwoThreadsAcquireOppositeOrderedKeys_AvoidsDeadlock()
    {
        using var context = CreateInMemoryDbContext();

        // Thread 1 wants { "Resource_A", "Resource_B" }
        // Thread 2 wants { "Resource_B", "Resource_A" }
        // Because of internal StringComparer.Ordinal sorting, both threads lock Resource_A then Resource_B, preventing AB-BA deadlock.

        int completedCount = 0;
        var startBarrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            startBarrier.SignalAndWait();
            using var ctx1 = CreateInMemoryDbContext();
            await using var lk = await ApplicationLockManager.AcquireLocksAsync(ctx1, new[] { "Resource_A", "Resource_B" }, TimeSpan.FromSeconds(5));
            lk.Should().NotBeNull();
            await Task.Delay(50);
            Interlocked.Increment(ref completedCount);
        });

        var task2 = Task.Run(async () =>
        {
            startBarrier.SignalAndWait();
            using var ctx2 = CreateInMemoryDbContext();
            await using var lk = await ApplicationLockManager.AcquireLocksAsync(ctx2, new[] { "Resource_B", "Resource_A" }, TimeSpan.FromSeconds(5));
            lk.Should().NotBeNull();
            await Task.Delay(50);
            Interlocked.Increment(ref completedCount);
        });

        await Task.WhenAll(task1, task2);
        completedCount.Should().Be(2);
    }
}
