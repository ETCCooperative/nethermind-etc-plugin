// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Consensus.Producers;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Runner for <c>EtcMining.Mode=Remote</c>. Builds the first block template on start so
/// <c>eth_getWork</c> serves work without requiring an initial <c>evm_mine</c>, and routes
/// manual production requests (<c>evm_mine</c>) through
/// <see cref="BuildBlocksContinuously"/> so they cancel the in-flight remote seal instead
/// of stalling on the producer lock.
/// </summary>
internal sealed class EtchashBlockProducerRunner(
    BuildBlocksContinuously workTrigger,
    IManualBlockProductionTrigger manualBlockProductionTrigger,
    IBlockTree blockTree,
    IBlockProducer blockProducer)
    : StandardBlockProducerRunner(workTrigger, blockTree, blockProducer)
{
    public override void Start()
    {
        base.Start();
        manualBlockProductionTrigger.TriggerBlockProduction += OnManualBlockProduction;
        workTrigger.BuildBlock();
    }

    public override Task StopAsync()
    {
        manualBlockProductionTrigger.TriggerBlockProduction -= OnManualBlockProduction;
        Task stopTask = base.StopAsync();
        workTrigger.Dispose();
        return stopTask;
    }

    private void OnManualBlockProduction(object? sender, BlockProductionEventArgs e) =>
        e.BlockProductionTask = workTrigger.BuildBlock(e.ParentHeader, e.CancellationToken, e.BlockTracer, e.PayloadAttributes);
}
