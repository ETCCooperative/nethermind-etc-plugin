// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    private readonly long _ecip1099Transition;
    private readonly Dictionary<uint, (uint DagEpoch, Task<IEthashDataSet> DataSet)> _cache = new(); // keyed by seedEpoch
    private readonly object _lock = new();
    private const long EtchashEpochLength = 60000;
    private readonly uint _transitionEpoch;

    public Etchash(ILogManager logManager, long ecip1099Transition)
    {
        _logger = logManager.GetClassLogger();
        _ecip1099Transition = ecip1099Transition;
        _transitionEpoch = (uint)(ecip1099Transition / EthashBase.EpochLength);
        if (_logger.IsInfo) _logger.Info($"Etchash initialized with ECIP-1099 transition at block {ecip1099Transition} (epoch {_transitionEpoch})");
    }

    private uint GetEtchashEpoch(long blockNumber) =>
        blockNumber < _ecip1099Transition
            ? (uint)(blockNumber / EthashBase.EpochLength)
            : (_transitionEpoch / 2) + (uint)((blockNumber - _ecip1099Transition) / EtchashEpochLength);

    private static uint GetSeedEpoch(uint dagEpoch, bool ecip1099Active) =>
        ecip1099Active ? dagEpoch * 2 : dagEpoch;

    public void HintRange(Guid guid, long start, long end)
    {
        uint startEpoch = GetEtchashEpoch(start);
        uint endEpoch = GetEtchashEpoch(end);
        bool ecip1099Active = start >= _ecip1099Transition;
        lock (_lock)
        {
            for (uint e = startEpoch; e <= endEpoch && e - startEpoch <= 10; e++)
            {
                uint dagEpoch = e;
                uint seedEpoch = GetSeedEpoch(dagEpoch, ecip1099Active);
                _cache.TryAdd(seedEpoch, (dagEpoch, Task.Run<IEthashDataSet>(() => new EthashCache(EthashBase.GetCacheSize(dagEpoch), EthashBase.GetSeedHash(seedEpoch).Bytes))));
            }
        }
    }

    public bool Validate(BlockHeader header)
    {
        uint dagEpoch = GetEtchashEpoch(header.Number);

        if (!TryGetDataSet(dagEpoch, header.Number, out var dataSet))
        {
            if (_logger.IsWarn) _logger.Warn($"Etchash cache miss for block {header.Number}, dagEpoch {dagEpoch}");
            return false;
        }

        ulong dataSize = EthashBase.GetDataSize(dagEpoch);
        var headerHash = Keccak.Compute(new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes);
        (byte[]? mixHash, _, bool valid) = EthashBase.Hashimoto(dataSize, dataSet, headerHash, header.MixHash, header.Nonce);

        if (!valid && _logger.IsWarn)
            _logger.Warn($"Etchash validation failed for block {header.Number}, dagEpoch {dagEpoch}, difficulty {header.Difficulty}");

        return valid;
    }

    public (Hash256, ulong) Mine(BlockHeader header, ulong? startNonce)
    {
        uint dagEpoch = GetEtchashEpoch(header.Number);
        bool ecip1099Active = header.Number >= _ecip1099Transition;
        uint seedEpoch = GetSeedEpoch(dagEpoch, ecip1099Active);

        TryGetDataSet(dagEpoch, header.Number, out var dataSet);
        dataSet ??= new EthashCache(EthashBase.GetCacheSize(dagEpoch), EthashBase.GetSeedHash(seedEpoch).Bytes);
        var headerHash = Keccak.Compute(new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes);
        ulong nonce = startNonce ?? (ulong)Random.Shared.NextInt64();
        while (true)
        {
            (byte[]? mix, _, bool ok) = EthashBase.Hashimoto(EthashBase.GetDataSize(dagEpoch), dataSet, headerHash, null, nonce);
            if (ok && mix is not null) return (new Hash256(mix), nonce);
            nonce++;
        }
    }

    private bool TryGetDataSet(uint dagEpoch, long blockNumber, out IEthashDataSet dataSet)
    {
        bool ecip1099Active = blockNumber >= _ecip1099Transition;
        uint seedEpoch = GetSeedEpoch(dagEpoch, ecip1099Active);
        lock (_lock) { if (_cache.TryGetValue(seedEpoch, out var t)) { dataSet = t.DataSet.Result; return true; } }
        HintRange(Guid.Empty, blockNumber, blockNumber);
        lock (_lock) { if (_cache.TryGetValue(seedEpoch, out var t)) { dataSet = t.DataSet.Result; return true; } }
        dataSet = null!; return false;
    }
}
