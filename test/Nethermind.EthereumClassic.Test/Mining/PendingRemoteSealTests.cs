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
public class PendingRemoteSealTests
{
    [Test]
    public async Task TryComplete_Ignores_Stale_Block()
    {
        object requestedBlock = new();
        object staleBlock = new();
        using PendingRemoteSeal<object> pendingSeal = new(requestedBlock, CancellationToken.None);

        pendingSeal.TryComplete(staleBlock).Should().BeFalse();
        pendingSeal.Task.IsCompleted.Should().BeFalse();

        pendingSeal.TryComplete(requestedBlock).Should().BeTrue();
        object sealedBlock = await pendingSeal.Task;
        sealedBlock.Should().BeSameAs(requestedBlock);
    }

    [Test]
    public void Already_Cancelled_Token_Cancels_Task_At_Construction()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        object requestedBlock = new();

        using PendingRemoteSeal<object> pendingSeal = new(requestedBlock, cts.Token);

        pendingSeal.Task.IsCanceled.Should().BeTrue();
        pendingSeal.TryComplete(requestedBlock).Should().BeFalse();
    }

    [Test]
    public async Task Token_Cancelled_After_Construction_Cancels_Task()
    {
        using CancellationTokenSource cts = new();
        object requestedBlock = new();
        using PendingRemoteSeal<object> pendingSeal = new(requestedBlock, cts.Token);

        pendingSeal.Task.IsCompleted.Should().BeFalse();

        cts.Cancel();

        Func<Task> awaitTask = () => pendingSeal.Task;
        await awaitTask.Should().ThrowAsync<OperationCanceledException>();
        pendingSeal.TryComplete(requestedBlock).Should().BeFalse();
    }

    [Test]
    public async Task Cancel_Method_Cancels_Pending_Task()
    {
        object requestedBlock = new();
        using PendingRemoteSeal<object> pendingSeal = new(requestedBlock, CancellationToken.None);

        pendingSeal.Cancel().Should().BeTrue();

        Func<Task> awaitTask = () => pendingSeal.Task;
        await awaitTask.Should().ThrowAsync<TaskCanceledException>();
        pendingSeal.TryComplete(requestedBlock).Should().BeFalse();
    }

    [Test]
    public async Task Cancel_After_Completion_Is_NoOp()
    {
        object requestedBlock = new();
        using PendingRemoteSeal<object> pendingSeal = new(requestedBlock, CancellationToken.None);

        pendingSeal.TryComplete(requestedBlock).Should().BeTrue();
        pendingSeal.Cancel().Should().BeFalse();

        object sealedBlock = await pendingSeal.Task;
        sealedBlock.Should().BeSameAs(requestedBlock);
    }
}
