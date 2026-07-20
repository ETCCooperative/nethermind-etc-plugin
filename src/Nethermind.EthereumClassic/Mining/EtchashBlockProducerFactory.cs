// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.EthereumClassic.Config;
using Nethermind.Logging;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Builds the Etchash block producer and its runner.
///
/// Replaces the pre-1.39.0 <c>IConsensusPlugin.InitBlockProducer</c> /
/// <c>InitBlockProducerRunner</c> plugin hooks, which Nethermind dropped in 1.39.0 in
/// favour of DI-registered <see cref="IBlockProducerFactory"/> /
/// <see cref="IBlockProducerRunnerFactory"/> (mirrors <c>EthashBlockProducerFactory</c> /
/// <c>NethDevBlockProducerFactory</c>). Only resolved once block production actually
/// starts, so <see cref="ISealer"/> (registered for every EtcMining.Mode except None) is
/// present whenever this factory is constructed.
/// </summary>
internal sealed class EtchashBlockProducerFactory(
    IBlockProducerEnvFactory blockProducerEnvFactory,
    IBlockTree blockTree,
    ITimestamper timestamper,
    ISpecProvider specProvider,
    IBlocksConfig blocksConfig,
    ISealer sealer,
    IDifficultyCalculator difficultyCalculator,
    IManualBlockProductionTrigger manualBlockProductionTrigger,
    IEtcMiningConfig miningConfig,
    IBlockProcessingQueue blockProcessingQueue,
    ILogManager logManager)
    : IBlockProducerFactory, IBlockProducerRunnerFactory
{
    public IBlockProducer InitBlockProducer()
    {
        IBlockProducerEnv env = blockProducerEnvFactory.CreatePersistent();
        return new EtchashBlockProducer(
            env.TxSource,
            env.ChainProcessor,
            env.ReadOnlyStateProvider,
            blockTree,
            timestamper,
            specProvider,
            blocksConfig,
            sealer,
            difficultyCalculator,
            logManager);
    }

    /// <summary>
    /// In <c>Remote</c> and <c>Local</c> modes production self-triggers (head change and
    /// periodic refresh): <c>Remote</c> continuously serves fresh work to
    /// <c>eth_getWork</c>, <c>Local</c> continuously CPU-mines. <c>Manual</c> mode
    /// produces blocks only on <c>evm_mine</c>, which
    /// <c>scripts/integration-test.sh</c> relies on for its exact block-height asserts.
    /// </summary>
    public IBlockProducerRunner InitBlockProducerRunner(IBlockProducer blockProducer)
    {
        if (miningConfig.Mode is EtcMiningMode.Remote or EtcMiningMode.Local)
        {
            BuildBlocksContinuously workTrigger = new(
                blockProcessingQueue,
                blockTree,
                TimeSpan.FromSeconds(miningConfig.WorkRefreshSeconds),
                logManager);
            return new EtchashBlockProducerRunner(workTrigger, manualBlockProductionTrigger, blockTree, blockProducer);
        }

        return new StandardBlockProducerRunner(manualBlockProductionTrigger, blockTree, blockProducer);
    }
}
