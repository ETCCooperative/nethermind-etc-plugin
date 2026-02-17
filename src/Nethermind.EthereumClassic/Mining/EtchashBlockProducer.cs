// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Evm.State;
using Nethermind.Logging;

namespace Nethermind.EthereumClassic.Mining;

internal sealed class EtchashBlockProducer(
    ITxSource txSource,
    IBlockchainProcessor processor,
    IWorldState stateProvider,
    IBlockTree blockTree,
    ITimestamper timestamper,
    ISpecProvider specProvider,
    IBlocksConfig blocksConfig,
    ISealer sealer,
    IDifficultyCalculator difficultyCalculator,
    ILogManager logManager)
    : BlockProducerBase(
        txSource,
        processor,
        sealer,
        blockTree,
        stateProvider,
        new FollowOtherMiners(specProvider),
        timestamper,
        specProvider,
        logManager,
        difficultyCalculator,
        blocksConfig);
