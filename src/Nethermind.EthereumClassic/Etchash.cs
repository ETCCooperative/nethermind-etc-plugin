// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Etchash (ECIP-1099): epoch length doubles from 30,000 to 60,000 blocks post-transition.
/// Uses standalone Ethash implementation to enable independent plugin distribution.
/// </summary>
internal class Etchash : IEthash
{
    private readonly ILogger _logger;
    private readonly EtchashEpochCalculator _epochCalculator;
    private readonly EtchashHintBasedCache _cache;

    public Etchash(ILogManager logManager, long ecip1099Transition)
    {
        _logger = logManager.GetClassLogger();
        _epochCalculator = new EtchashEpochCalculator(ecip1099Transition);
        _cache = new EtchashHintBasedCache(BuildCache);
        if (_logger.IsInfo) _logger.Info($"Etchash initialized with ECIP-1099 transition at block {ecip1099Transition} (epoch {_epochCalculator.TransitionEpoch})");
    }

    public void HintRange(Guid guid, long start, long end)
    {
        _cache.Hint(guid, _epochCalculator.GetCacheEpochs(start, end, EtchashHintBasedCache.MaxHintEpochs));
    }

    public bool Validate(BlockHeader header)
    {
        EtchashCacheEpoch cacheEpoch = _epochCalculator.GetCacheEpoch(header.Number);

        if (!TryGetDataSet(cacheEpoch, header.Number, out var dataSet))
        {
            if (_logger.IsWarn) _logger.Warn($"Etchash cache miss for block {header.Number}, dagEpoch {cacheEpoch.DagEpoch}");
            return false;
        }

        ulong dataSize = EthashBase.GetDataSize(cacheEpoch.DagEpoch);
        var headerHash = Keccak.Compute(new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes);
        (_, ValueHash256 result, bool mixHashOk) = EthashBase.Hashimoto(dataSize, dataSet, headerHash, header.MixHash, header.Nonce);
        bool meetsTarget = EthashBase.IsValidPoWResult(mixHashOk, result.Bytes, header.Difficulty);

        if (!meetsTarget && _logger.IsWarn)
            _logger.Warn($"Etchash validation failed for block {header.Number}, dagEpoch {cacheEpoch.DagEpoch}, difficulty {header.Difficulty}");

        return meetsTarget;
    }

    public (Hash256, ulong) Mine(BlockHeader header, ulong? startNonce)
    {
        EtchashCacheEpoch cacheEpoch = _epochCalculator.GetCacheEpoch(header.Number);

        TryGetDataSet(cacheEpoch, header.Number, out var dataSet);
        dataSet ??= BuildCache(cacheEpoch);
        var headerHash = Keccak.Compute(new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes);
        ulong nonce = startNonce ?? (ulong)Random.Shared.NextInt64();
        ulong dataSize = EthashBase.GetDataSize(cacheEpoch.DagEpoch);
        while (true)
        {
            (byte[]? mix, ValueHash256 result, bool ok) = EthashBase.Hashimoto(dataSize, dataSet, headerHash, null, nonce);
            if (EthashBase.IsValidMiningResult(mix, ok, result.Bytes, header.Difficulty))
                return (new Hash256(mix), nonce);

            nonce++;
        }
    }

    private bool TryGetDataSet(EtchashCacheEpoch cacheEpoch, long blockNumber, out IEthashDataSet dataSet)
    {
        IEthashDataSet? cached = _cache.Get(cacheEpoch);
        if (cached is not null)
        {
            dataSet = cached;
            return true;
        }

        HintRange(Guid.Empty, blockNumber, blockNumber);
        cached = _cache.Get(cacheEpoch);
        if (cached is not null)
        {
            dataSet = cached;
            return true;
        }

        dataSet = null!;
        return false;
    }

    private static IEthashDataSet BuildCache(EtchashCacheEpoch epoch) =>
        new EthashCache(EthashBase.GetCacheSize(epoch.DagEpoch), EthashBase.GetSeedHash(epoch.SeedEpoch).Bytes);
}
