// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Int256;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Pure calculation methods for ECIP-1017 monetary policy.
/// This class has no dependencies on Nethermind runtime types and can be tested in isolation.
/// </summary>
public static class Ecip1017Calculator
{
    /// <summary>
    /// Base block reward: 5 ETC = 5 * 10^18 wei.
    /// </summary>
    public static readonly UInt256 BaseReward = 5_000_000_000_000_000_000;

    /// <summary>
    /// Calculates the block reward for a given block number according to ECIP-1017.
    /// Era 1: blocks 1 to eraPeriod → 5 ETC
    /// Era 2: blocks eraPeriod+1 to 2*eraPeriod → 4 ETC (5 * 0.8)
    /// Era N: 5 ETC * (4/5)^(N-1)
    /// </summary>
    /// <param name="blockNumber">The block number.</param>
    /// <param name="eraPeriod">Era period in blocks (5M for mainnet, 2M for Mordor).</param>
    /// <returns>Block reward in wei.</returns>
    public static UInt256 CalculateBlockReward(long blockNumber, long eraPeriod)
    {
        if (blockNumber <= 0)
        {
            return BaseReward;
        }

        // Era is 1-indexed: Era 1 = blocks 1-eraPeriod, Era 2 = blocks eraPeriod+1-2*eraPeriod, etc.
        // For calculation, we use 0-indexed era: era0 = (blockNumber - 1) / eraPeriod
        long era = (blockNumber - 1) / eraPeriod;

        // Calculate reward = 5 ETC * (4/5)^era using integer math
        // To avoid overflow, we apply the reduction iteratively
        UInt256 reward = BaseReward;
        for (long i = 0; i < era; i++)
        {
            // reward = reward * 4 / 5
            reward = reward * 4 / 5;
        }

        return reward;
    }

    /// <summary>
    /// Gets the 0-indexed era number for a given block.
    /// Era 0 = blocks 1 to eraPeriod, Era 1 = blocks eraPeriod+1 to 2*eraPeriod, etc.
    /// </summary>
    /// <param name="blockNumber">The block number.</param>
    /// <param name="eraPeriod">Era period in blocks.</param>
    /// <returns>0-indexed era number.</returns>
    public static long GetEra(long blockNumber, long eraPeriod)
    {
        if (blockNumber <= 0) return 0;
        return (blockNumber - 1) / eraPeriod;
    }

    /// <summary>
    /// Calculates the uncle reward according to ECIP-1017.
    /// Era 0: Standard Ethereum formula: blockReward * (8 - distance) / 8
    /// Era 1+: Fixed 1/32 of block reward
    /// </summary>
    /// <param name="blockReward">The block reward for the current block.</param>
    /// <param name="blockNumber">The block number containing the uncle.</param>
    /// <param name="uncleNumber">The uncle block number.</param>
    /// <param name="era">The 0-indexed era number.</param>
    /// <returns>Uncle reward in wei.</returns>
    public static UInt256 CalculateUncleReward(UInt256 blockReward, long blockNumber, long uncleNumber, long era)
    {
        if (era == 0)
        {
            // Era 1: Standard Ethereum uncle reward formula
            // Uncle reward = blockReward * (8 - distance) / 8
            return blockReward - ((uint)(blockNumber - uncleNumber) * blockReward >> 3);
        }

        // Era 2+: ECIP-1017 changed uncle reward to fixed 1/32 of block reward
        return blockReward >> 5;
    }
}
