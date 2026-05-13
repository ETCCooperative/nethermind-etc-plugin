// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace Nethermind.EthereumClassic;

internal sealed class EtchashEpochCalculator
{
    private const long EtchashEpochLength = 60_000;

    private readonly long _ecip1099Transition;
    private readonly uint _transitionEpoch;

    public EtchashEpochCalculator(long ecip1099Transition)
    {
        _ecip1099Transition = ecip1099Transition;
        _transitionEpoch = (uint)(ecip1099Transition / EthashBase.EpochLength);
    }

    public uint TransitionEpoch => _transitionEpoch;

    public EtchashCacheEpoch GetCacheEpoch(long blockNumber)
    {
        uint dagEpoch = GetDagEpoch(blockNumber);
        return new EtchashCacheEpoch(dagEpoch, GetSeedEpoch(dagEpoch, blockNumber >= _ecip1099Transition));
    }

    public IReadOnlyList<EtchashCacheEpoch> GetCacheEpochs(long startBlock, long endBlock, int maxEpochs = int.MaxValue)
    {
        if (endBlock < startBlock)
            throw new ArgumentOutOfRangeException(nameof(endBlock), "End block must be greater than or equal to start block.");

        List<EtchashCacheEpoch> epochs = [];

        if (startBlock < _ecip1099Transition)
        {
            long preTransitionEnd = Math.Min(endBlock, _ecip1099Transition - 1);
            if (preTransitionEnd >= startBlock)
            {
                uint startEpoch = GetDagEpoch(startBlock);
                uint endEpoch = GetDagEpoch(preTransitionEnd);
                for (uint epoch = startEpoch; epoch <= endEpoch; epoch++)
                {
                    AddEpoch(epochs, new EtchashCacheEpoch(epoch, epoch), maxEpochs);
                }
            }
        }

        if (endBlock >= _ecip1099Transition)
        {
            long postTransitionStart = Math.Max(startBlock, _ecip1099Transition);
            uint startEpoch = GetDagEpoch(postTransitionStart);
            uint endEpoch = GetDagEpoch(endBlock);
            for (uint epoch = startEpoch; epoch <= endEpoch; epoch++)
            {
                AddEpoch(epochs, new EtchashCacheEpoch(epoch, GetSeedEpoch(epoch, ecip1099Active: true)), maxEpochs);
            }
        }

        return epochs;
    }

    private static void AddEpoch(List<EtchashCacheEpoch> epochs, EtchashCacheEpoch epoch, int maxEpochs)
    {
        if (epochs.Count >= maxEpochs)
            throw new InvalidOperationException("Hint too wide");

        epochs.Add(epoch);
    }

    private uint GetDagEpoch(long blockNumber) =>
        blockNumber < _ecip1099Transition
            ? (uint)(blockNumber / EthashBase.EpochLength)
            : (_transitionEpoch / 2) + (uint)((blockNumber - _ecip1099Transition) / EtchashEpochLength);

    private static uint GetSeedEpoch(uint dagEpoch, bool ecip1099Active) =>
        ecip1099Active ? dagEpoch * 2 : dagEpoch;
}
