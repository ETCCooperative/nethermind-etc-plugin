// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Specs;
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
/// starts, so <see cref="ISealer"/> (registered only for EtcMining.Mode Remote/Manual) is
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

    public IBlockProducerRunner InitBlockProducerRunner(IBlockProducer blockProducer) =>
        new StandardBlockProducerRunner(manualBlockProductionTrigger, blockTree, blockProducer);
}
