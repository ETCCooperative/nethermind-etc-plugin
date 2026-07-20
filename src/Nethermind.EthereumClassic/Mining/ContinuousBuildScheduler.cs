// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Scheduling policy of <see cref="BuildBlocksContinuously"/>: decides when a block
/// template (re)build should fire. A build fires immediately when production is
/// un-gated and the processing queue is idle; otherwise a single delayed waiter polls
/// until the queue drains and fires exactly one build. Periodic refresh ticks fire a
/// build only while idle, so they never pile up behind block processing. Kept free of
/// Nethermind types (dependencies are plain delegates) so it is unit-testable: the
/// test project compiles against reference assemblies and cannot load Nethermind
/// runtime types.
/// </summary>
internal sealed class ContinuousBuildScheduler : IDisposable
{
    private readonly Func<bool> _isQueueEmpty;
    private readonly Action _requestBuild;
    private readonly Action<bool> _canProduceChanged;
    private readonly Action? _waitingForQueue;
    private readonly TimeSpan _queueRetryDelay;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly CancellationToken _disposeToken;
    private int _canProduce = 1;
    private int _delayedBuildScheduled;

    public ContinuousBuildScheduler(
        Func<bool> isQueueEmpty,
        Action requestBuild,
        Action<bool> canProduceChanged,
        TimeSpan queueRetryDelay,
        Action? waitingForQueue = null)
    {
        _isQueueEmpty = isQueueEmpty;
        _requestBuild = requestBuild;
        _canProduceChanged = canProduceChanged;
        _queueRetryDelay = queueRetryDelay;
        _waitingForQueue = waitingForQueue;
        // Captured once: reading CancellationTokenSource.Token after Dispose throws.
        _disposeToken = _disposeCancellation.Token;
    }

    public bool CanTriggerBuild => _canProduce == 1 && _isQueueEmpty();

    /// <summary>
    /// A new best block was suggested. Production is gated until it finishes
    /// processing, unless it is already the head.
    /// </summary>
    public void NotifyBlockSuggested(bool alreadyProcessed)
    {
        Interlocked.Exchange(ref _canProduce, alreadyProcessed ? 1 : 0);
        _canProduceChanged(alreadyProcessed);
        ScheduleBuild();
    }

    /// <summary>The processing queue drained; production is un-gated.</summary>
    public void NotifyQueueEmpty()
    {
        Interlocked.Exchange(ref _canProduce, 1);
        _canProduceChanged(true);
        ScheduleBuild();
    }

    /// <summary>Periodic refresh: rebuild the current template only while idle.</summary>
    public void NotifyRefreshTick()
    {
        if (CanTriggerBuild)
        {
            _requestBuild();
        }
    }

    private void ScheduleBuild()
    {
        if (_delayedBuildScheduled == 1)
        {
            return;
        }

        if (CanTriggerBuild)
        {
            _requestBuild();
        }
        else if (Interlocked.CompareExchange(ref _delayedBuildScheduled, 1, 0) == 0)
        {
            _ = BuildWhenQueueDrains();
        }
    }

    private async Task BuildWhenQueueDrains()
    {
        try
        {
            while (!CanTriggerBuild)
            {
                _waitingForQueue?.Invoke();
                await Task.Delay(_queueRetryDelay, _disposeToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            Interlocked.Exchange(ref _delayedBuildScheduled, 0);
        }

        _requestBuild();
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
    }
}
