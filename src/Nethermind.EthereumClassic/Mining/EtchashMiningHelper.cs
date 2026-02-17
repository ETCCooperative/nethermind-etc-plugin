// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Numerics;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Pure static helper methods for Etchash mining calculations.
/// Separated from RemoteSealerClient to enable testing without Nethermind runtime dependencies.
/// </summary>
internal static class EtchashMiningHelper
{
    internal const long EpochLength = 30000;
    internal const long EtchashEpochLength = 60000;

    internal static readonly BigInteger TwoTo256 = BigInteger.Pow(2, 256);

    /// <summary>
    /// Computes the Etchash DAG epoch for a given block number.
    /// Before ECIP-1099: epoch = blockNumber / 30000
    /// After ECIP-1099: epoch continues with 60000-block epochs
    /// </summary>
    internal static uint GetEtchashEpoch(long blockNumber, long ecip1099Transition, uint transitionEpoch) =>
        blockNumber < ecip1099Transition
            ? (uint)(blockNumber / EpochLength)
            : (transitionEpoch / 2) + (uint)((blockNumber - ecip1099Transition) / EtchashEpochLength);

    /// <summary>
    /// Converts a DAG epoch to a seed epoch for seed hash computation.
    /// After ECIP-1099, the seed epoch is doubled because the DAG epoch length was doubled.
    /// </summary>
    internal static uint GetSeedEpoch(uint dagEpoch, bool ecip1099Active) =>
        ecip1099Active ? dagEpoch * 2 : dagEpoch;

    /// <summary>
    /// Computes the mining target as 2^256 / difficulty, returned as a 32-byte big-endian array.
    /// </summary>
    internal static byte[] ComputeTargetBytes(in BigInteger difficulty)
    {
        BigInteger target = TwoTo256 / difficulty;

        byte[] targetBytes = new byte[32];
        byte[] rawBytes = target.ToByteArray(isUnsigned: true, isBigEndian: true);

        int offset = 32 - rawBytes.Length;
        if (offset >= 0)
        {
            Array.Copy(rawBytes, 0, targetBytes, offset, rawBytes.Length);
        }
        else
        {
            Array.Fill(targetBytes, (byte)0xFF);
        }

        return targetBytes;
    }
}
