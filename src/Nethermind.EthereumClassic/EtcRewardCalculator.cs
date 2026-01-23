// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Consensus.Rewards;
using Nethermind.Core;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Int256;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Reward calculator for Ethereum Classic implementing ECIP-1017 monetary policy.
/// Calculates block rewards using the era system (20% reduction every era).
/// Era period is configurable: ETC mainnet uses 5M blocks, Mordor uses 2M blocks.
/// This operates independently of the spec system to avoid Fork ID conflicts per ECIP-1082.
/// </summary>
public class EtcRewardCalculator : IRewardCalculator, IRewardCalculatorSource
{
    private readonly long _eraPeriod;

    public EtcRewardCalculator(long eraPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eraPeriod);
        _eraPeriod = eraPeriod;
    }

    public BlockReward[] CalculateRewards(Block block)
    {
        if (block.IsGenesis)
        {
            return [];
        }

        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(block.Number, _eraPeriod);
        BlockReward[] rewards = new BlockReward[1 + block.Uncles.Length];

        // Nephew reward (bonus for including uncles) is 1/32 per uncle in all eras
        BlockHeader blockHeader = block.Header;
        UInt256 mainReward = blockReward + (uint)block.Uncles.Length * (blockReward >> 5);
        rewards[0] = new BlockReward(blockHeader.Beneficiary, mainReward);

        // Era determines uncle reward formula
        long era = Ecip1017Calculator.GetEra(block.Number, _eraPeriod);

        for (int i = 0; i < block.Uncles.Length; i++)
        {
            UInt256 uncleReward = Ecip1017Calculator.CalculateUncleReward(
                blockReward, blockHeader.Number, block.Uncles[i].Number, era);
            rewards[i + 1] = new BlockReward(block.Uncles[i].Beneficiary, uncleReward, BlockRewardType.Uncle);
        }

        return rewards;
    }

    public IRewardCalculator Get(ITransactionProcessor processor) => this;
}
