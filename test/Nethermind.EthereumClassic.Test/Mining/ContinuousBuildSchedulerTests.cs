// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.EthereumClassic.Mining;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test.Mining;

[TestFixture]
public class ContinuousBuildSchedulerTests
{
    private sealed class Harness : IDisposable
    {
        private int _builds;
        private volatile bool _queueEmpty = true;

        public Harness()
        {
            Scheduler = new ContinuousBuildScheduler(
                isQueueEmpty: () => _queueEmpty,
                requestBuild: () => Interlocked.Increment(ref _builds),
                canProduceChanged: CanProduceChanges.Add,
                queueRetryDelay: TimeSpan.FromMilliseconds(1));
        }

        public ContinuousBuildScheduler Scheduler { get; }

        public List<bool> CanProduceChanges { get; } = [];

        public int Builds => _builds;

        public bool QueueEmpty
        {
            get => _queueEmpty;
            set => _queueEmpty = value;
        }

        public async Task WaitForBuilds(int expected)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Builds < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }

            Builds.Should().Be(expected);
        }

        /// <summary>Asserts the build count stays at the given value for a grace period.</summary>
        public async Task AssertNoFurtherBuilds(int expected)
        {
            await Task.Delay(50);
            Builds.Should().Be(expected);
        }

        public void Dispose() => Scheduler.Dispose();
    }

    [Test]
    public void Builds_Immediately_When_Idle()
    {
        using Harness harness = new();

        harness.Scheduler.NotifyQueueEmpty();

        harness.Builds.Should().Be(1);
    }

    [Test]
    public void Suggested_Block_Already_Processed_Builds_Immediately()
    {
        using Harness harness = new();

        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: true);

        harness.Builds.Should().Be(1);
    }

    [Test]
    public async Task Suggested_Block_Gates_Build_Until_Queue_Drains()
    {
        using Harness harness = new();
        harness.QueueEmpty = false;

        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        await harness.AssertNoFurtherBuilds(0);

        harness.QueueEmpty = true;
        harness.Scheduler.NotifyQueueEmpty();

        await harness.WaitForBuilds(1);
        await harness.AssertNoFurtherBuilds(1);
    }

    [Test]
    public async Task Multiple_Notifications_While_Gated_Yield_Single_Build()
    {
        using Harness harness = new();
        harness.QueueEmpty = false;

        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        await harness.AssertNoFurtherBuilds(0);

        harness.QueueEmpty = true;
        harness.Scheduler.NotifyQueueEmpty();

        await harness.WaitForBuilds(1);
        await harness.AssertNoFurtherBuilds(1);
    }

    [Test]
    public async Task Refresh_Tick_Builds_Only_While_Idle()
    {
        using Harness harness = new();

        harness.Scheduler.NotifyRefreshTick();
        harness.Builds.Should().Be(1);

        harness.QueueEmpty = false;
        harness.Scheduler.NotifyRefreshTick();
        await harness.AssertNoFurtherBuilds(1);
    }

    [Test]
    public async Task Refresh_Tick_Respects_Production_Gate()
    {
        using Harness harness = new();
        harness.QueueEmpty = false;
        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        harness.QueueEmpty = true;

        // The queue is idle again but the gate is still down: nothing was processed yet.
        harness.Scheduler.NotifyRefreshTick();

        // The delayed waiter is also still gated, so no build may fire.
        harness.Scheduler.CanTriggerBuild.Should().BeFalse();
        await harness.AssertNoFurtherBuilds(0);

        harness.Scheduler.NotifyQueueEmpty();
        await harness.WaitForBuilds(1);
    }

    [Test]
    public async Task Dispose_Cancels_Pending_Delayed_Build()
    {
        Harness harness = new();
        harness.QueueEmpty = false;
        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);

        harness.Dispose();
        harness.QueueEmpty = true;

        await harness.AssertNoFurtherBuilds(0);
    }

    [Test]
    public void Reports_Gate_Transitions()
    {
        using Harness harness = new();

        harness.Scheduler.NotifyBlockSuggested(alreadyProcessed: false);
        harness.Scheduler.NotifyQueueEmpty();

        harness.CanProduceChanges.Should().Equal(false, true);
    }
}
