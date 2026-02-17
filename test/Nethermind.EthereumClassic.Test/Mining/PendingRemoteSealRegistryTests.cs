// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.EthereumClassic.Mining;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test.Mining;

[TestFixture]
public class PendingRemoteSealRegistryTests
{
    [Test]
    public async Task Replace_Cancels_Previous_Pending_Seal()
    {
        PendingRemoteSealRegistry<object> registry = new();
        object firstBlock = new();
        object secondBlock = new();

        PendingRemoteSeal<object> first = registry.Replace(firstBlock, CancellationToken.None);
        PendingRemoteSeal<object> second = registry.Replace(secondBlock, CancellationToken.None);

        Func<Task> awaitFirst = () => first.Task;
        await awaitFirst.Should().ThrowAsync<TaskCanceledException>();
        second.Task.IsCompleted.Should().BeFalse();
    }

    [Test]
    public async Task TryCompleteActive_Completes_Current_Pending_Seal()
    {
        PendingRemoteSealRegistry<object> registry = new();
        object firstBlock = new();
        object secondBlock = new();

        registry.Replace(firstBlock, CancellationToken.None);
        PendingRemoteSeal<object> second = registry.Replace(secondBlock, CancellationToken.None);

        registry.TryCompleteActive(secondBlock).Should().BeTrue();
        (await second.Task).Should().BeSameAs(secondBlock);
    }

    [Test]
    public void TryCompleteActive_Rejects_Stale_Block_Without_Touching_Current()
    {
        PendingRemoteSealRegistry<object> registry = new();
        object firstBlock = new();
        object secondBlock = new();

        registry.Replace(firstBlock, CancellationToken.None);
        PendingRemoteSeal<object> second = registry.Replace(secondBlock, CancellationToken.None);

        registry.TryCompleteActive(firstBlock).Should().BeFalse();
        second.Task.IsCompleted.Should().BeFalse();
    }

    [Test]
    public void TryCompleteActive_Returns_False_When_Empty()
    {
        PendingRemoteSealRegistry<object> registry = new();
        registry.TryCompleteActive(new object()).Should().BeFalse();
    }

    [Test]
    public void TryCompleteActive_Clears_Active_After_Successful_Completion()
    {
        PendingRemoteSealRegistry<object> registry = new();
        object block = new();
        registry.Replace(block, CancellationToken.None);

        registry.TryCompleteActive(block).Should().BeTrue();
        registry.TryCompleteActive(block).Should().BeFalse();
    }

    [Test]
    public async Task Concurrent_Replace_And_TryCompleteActive_Leaves_Old_Pending_Cancelled()
    {
        const int iterations = 256;
        for (int i = 0; i < iterations; i++)
        {
            PendingRemoteSealRegistry<object> registry = new();
            object firstBlock = new();
            object secondBlock = new();
            PendingRemoteSeal<object> first = registry.Replace(firstBlock, CancellationToken.None);

            using Barrier barrier = new(2);
            Task replaceTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                registry.Replace(secondBlock, CancellationToken.None);
            });
            Task<bool> completeTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return registry.TryCompleteActive(firstBlock);
            });

            await Task.WhenAll(replaceTask, completeTask);

            // Whichever thread acquires the lock first decides the outcome: if TryCompleteActive
            // wins, the old pending completes with its own block; if Replace wins, the old pending
            // is cancelled and TryCompleteActive sees the new active and returns false. In either
            // case the old pending lands in a terminal state and never dangles.
            if (completeTask.Result)
            {
                first.Task.Status.Should().Be(TaskStatus.RanToCompletion);
                (await first.Task).Should().BeSameAs(firstBlock);
            }
            else
            {
                Func<Task> awaitFirst = () => first.Task;
                await awaitFirst.Should().ThrowAsync<TaskCanceledException>();
            }
        }
    }
}
