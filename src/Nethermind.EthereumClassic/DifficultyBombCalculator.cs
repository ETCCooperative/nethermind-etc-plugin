// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Pure calculation methods for Ethereum Classic difficulty bomb.
/// This class has no dependencies on Nethermind runtime types and can be tested in isolation.
/// </summary>
public static class DifficultyBombCalculator
{
    /// <summary>
    /// The block number where the difficulty bomb first becomes active.
    /// </summary>
    public const long InitialBombBlock = 200_000;

    /// <summary>
    /// The exponential difficulty period (100,000 blocks).
    /// </summary>
    public const long ExponentialPeriod = 100_000;

    /// <summary>
    /// Calculates the difficulty bomb contribution for a given block.
    /// </summary>
    /// <param name="blockNumber">The block number.</param>
    /// <param name="dieHardBlock">DieHard fork block (bomb paused), or null if never existed.</param>
    /// <param name="gothamBlock">Gotham fork block (bomb delayed), or null if not applicable.</param>
    /// <param name="ecip1041Block">ECIP-1041 block (bomb removed), or null if not applicable.</param>
    /// <returns>The difficulty bomb value to add to the difficulty calculation.</returns>
    public static BigInteger CalculateTimeBomb(
        long blockNumber,
        long? dieHardBlock,
        long? gothamBlock,
        long? ecip1041Block)
    {
        // If ECIP-1041 is active, bomb is removed
        if (ecip1041Block is not null && blockNumber >= ecip1041Block)
            return BigInteger.Zero;

        // If no DieHard defined, bomb never existed (e.g., Mordor)
        if (dieHardBlock is null)
            return BigInteger.Zero;

        long period = blockNumber / ExponentialPeriod;

        // Gotham: bomb delayed
        if (gothamBlock is not null && blockNumber >= gothamBlock)
        {
            long bombDelay = (gothamBlock.Value - dieHardBlock.Value) / ExponentialPeriod;
            return period - bombDelay - 2 < 0
                ? BigInteger.Zero
                : BigInteger.Pow(2, (int)(period - bombDelay - 2));
        }

        // Die Hard: bomb paused at fixed period
        if (blockNumber >= dieHardBlock)
        {
            long fixedPeriod = dieHardBlock.Value / ExponentialPeriod;
            return BigInteger.Pow(2, (int)(fixedPeriod - 2));
        }

        // Pre-Die Hard: normal bomb
        if (blockNumber < InitialBombBlock)
            return BigInteger.Zero;

        return period < 2 ? BigInteger.Zero : BigInteger.Pow(2, (int)(period - 2));
    }
}
