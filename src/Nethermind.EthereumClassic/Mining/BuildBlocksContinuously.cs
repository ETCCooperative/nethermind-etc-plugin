// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Evm.Tracing;
using Nethermind.Logging;
using Metrics = Nethermind.Blockchain.Metrics;
using Timer = System.Timers.Timer;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Continuously re-arms Etchash block production the way geth's miner does:
/// a new block template is built once the processing queue drains after a chain-head
/// change, and the current template is refreshed periodically so transactions arriving
/// between blocks get picked up. Every (re)trigger cancels the previous in-flight
/// production, which releases the pending seal so a fresh template can be built — in
/// <c>Remote</c> mode the seal held by <see cref="RemoteEtchashSealer"/>, without which
/// <c>BlockProducerBase</c> would hold its production lock until an external miner
/// submits a solution and block production would wedge on the first template forever;
/// in <c>Local</c> mode the CPU search of <see cref="LocalEtchashSealer"/>, which loses
/// no progress on restart because the PoW nonce search is memoryless. Adapted from the
/// <c>BuildBlocksWhenProcessingFinished</c> trigger that upstream Nethermind removed
/// together with Ethash mining in 2022, except that production starts armed: on an idle
/// chain nothing fires <c>ProcessingQueueEmpty</c> after startup, and mining must start
/// (and remote mining must serve work) immediately. The when-to-build policy lives in
/// <see cref="ContinuousBuildScheduler"/>; this class wires it to the Nethermind events
/// and owns the production cancellation chain and the refresh timer.
/// </summary>
internal sealed class BuildBlocksContinuously : IManualBlockProductionTrigger, IDisposable
{
    private const int ChainNotYetProcessedMillisecondsDelay = 100;

    private readonly IBlockProcessingQueue _blockProcessingQueue;
    private readonly IBlockTree _blockTree;
    private readonly ILogger _logger;
    private readonly ContinuousBuildScheduler _scheduler;
    private readonly Timer _refreshTimer;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _productionCancellation;
    private int _disposed;

    public BuildBlocksContinuously(
        IBlockProcessingQueue blockProcessingQueue,
        IBlockTree blockTree,
        TimeSpan refreshInterval,
        ILogManager logManager)
    {
        _blockProcessingQueue = blockProcessingQueue;
        _blockTree = blockTree;
        _logger = logManager.GetClassLogger<BuildBlocksContinuously>();

        _scheduler = new ContinuousBuildScheduler(
            isQueueEmpty: () => _blockProcessingQueue.IsEmpty,
            requestBuild: () => BuildBlock(),
            canProduceChanged: canProduce => Interlocked.Exchange(ref Metrics.CanProduceBlocks, canProduce ? 1 : 0),
            queueRetryDelay: TimeSpan.FromMilliseconds(ChainNotYetProcessedMillisecondsDelay),
            waitingForQueue: () =>
            {
                if (_logger.IsDebug) _logger.Debug($"Delaying block template rebuild, {_blockProcessingQueue.Count} blocks in the processing queue");
            });

        _blockProcessingQueue.ProcessingQueueEmpty += OnProcessingQueueEmpty;
        _blockTree.NewBestSuggestedBlock += OnNewBestSuggestedBlock;

        _refreshTimer = new Timer(refreshInterval.TotalMilliseconds) { AutoReset = false };
        _refreshTimer.Elapsed += OnRefreshTimerElapsed;
        _refreshTimer.Start();
    }

    public event EventHandler<BlockProductionEventArgs>? TriggerBlockProduction;

    public Task<Block?> BuildBlock(
        BlockHeader? parentHeader = null,
        CancellationToken? cancellationToken = null,
        IBlockTracer? blockTracer = null,
        PayloadAttributes? payloadAttributes = null)
    {
        lock (_lock)
        {
            CancelPreviousBlockProduction();
            _productionCancellation = cancellationToken is not null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Value)
                : new CancellationTokenSource();

            BlockProductionEventArgs eventArgs = new(parentHeader, _productionCancellation.Token, blockTracer, payloadAttributes);
            TriggerBlockProduction?.Invoke(this, eventArgs);
            return eventArgs.BlockProductionTask;
        }
    }

    private void OnNewBestSuggestedBlock(object? sender, BlockEventArgs e) =>
        _scheduler.NotifyBlockSuggested(alreadyProcessed: _blockTree.Head?.Hash == e.Block.Hash);

    private void OnProcessingQueueEmpty(object? sender, EventArgs e) => _scheduler.NotifyQueueEmpty();

    private void OnRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _scheduler.NotifyRefreshTick();

        if (_disposed == 0)
        {
            _refreshTimer.Enabled = true;
        }
    }

    private void CancelPreviousBlockProduction()
    {
        CancellationTokenSource? previous = _productionCancellation;
        _productionCancellation = null;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _refreshTimer.Dispose();
        _blockTree.NewBestSuggestedBlock -= OnNewBestSuggestedBlock;
        _blockProcessingQueue.ProcessingQueueEmpty -= OnProcessingQueueEmpty;
        _scheduler.Dispose();
        lock (_lock)
        {
            CancelPreviousBlockProduction();
        }
    }
}
