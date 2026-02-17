// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;

[assembly: InternalsVisibleTo("Nethermind.EthereumClassic.Test")]

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Manages remote mining work for eth_getWork/eth_submitWork.
/// </summary>
internal sealed class RemoteSealerClient : IRemoteSealerClient
{
    private const int MaxRecentWorkItems = 8;

    private readonly IEthash _ethash;
    private readonly long _ecip1099Transition;
    private readonly uint _transitionEpoch;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Hash256, Block> _recentWork = new();
    private readonly object _lock = new();

    private Block? _currentBlock;
    private MiningWork? _currentWork;
    private Action<Block>? _onBlockMined;

    public RemoteSealerClient(IEthash ethash, long ecip1099Transition, ILogManager logManager)
    {
        _ethash = ethash;
        _ecip1099Transition = ecip1099Transition;
        _transitionEpoch = (uint)(ecip1099Transition / EtchashMiningHelper.EpochLength);
        _logger = logManager.GetClassLogger();
    }

    public MiningWork? GetWork()
    {
        lock (_lock)
        {
            return _currentWork;
        }
    }

    public bool SubmitWork(PoWSolution solution)
    {
        if (!_recentWork.TryGetValue(solution.PowHash, out Block? block))
        {
            if (_logger.IsDebug) _logger.Debug($"SubmitWork: unknown pow-hash {solution.PowHash}");
            return false;
        }

        // Apply solution to block header
        block.Header.Nonce = solution.Nonce;
        block.Header.MixHash = solution.MixHash;

        // Validate the solution
        if (!_ethash.Validate(block.Header))
        {
            if (_logger.IsDebug) _logger.Debug($"SubmitWork: invalid solution for block {block.Number}");
            return false;
        }

        // Calculate the final block hash
        block.Header.Hash = block.Header.CalculateHash();

        if (_logger.IsInfo) _logger.Info($"SubmitWork: valid solution for block {block.Number}, hash={block.Hash}");

        // Remove from recent work
        _recentWork.TryRemove(solution.PowHash, out _);

        // Clear current work if this was it
        lock (_lock)
        {
            if (_currentWork?.PowHash == solution.PowHash)
            {
                _currentWork = null;
                _currentBlock = null;
            }
        }

        // Notify callback
        _onBlockMined?.Invoke(block);

        return true;
    }

    public void SubmitNewWork(Block block)
    {
        Hash256 powHash = ComputePowHash(block.Header);
        Hash256 seedHash = ComputeSeedHash(block.Number);
        Hash256 target = ComputeTarget(block.Header.Difficulty);

        var work = new MiningWork(powHash, seedHash, target, block.Number);

        lock (_lock)
        {
            _currentBlock = block;
            _currentWork = work;
        }

        // Store in recent work cache
        _recentWork[powHash] = block;

        // Clean up old work items if needed
        CleanupOldWork();

        if (_logger.IsDebug) _logger.Debug($"SubmitNewWork: block {block.Number}, powHash={powHash}, target={target}");
    }

    public void SetOnBlockMined(Action<Block> callback)
    {
        _onBlockMined = callback;
    }

    private static Hash256 ComputePowHash(BlockHeader header)
    {
        // Encode header without nonce/mixHash (ForSealing)
        byte[] encoded = new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes;
        return Keccak.Compute(encoded);
    }

    private Hash256 ComputeSeedHash(long blockNumber)
    {
        uint dagEpoch = GetEtchashEpoch(blockNumber);
        bool ecip1099Active = blockNumber >= _ecip1099Transition;
        uint seedEpoch = EtchashMiningHelper.GetSeedEpoch(dagEpoch, ecip1099Active);
        return EthashBase.GetSeedHash(seedEpoch);
    }

    private static Hash256 ComputeTarget(in Nethermind.Int256.UInt256 difficulty)
    {
        if (difficulty.IsZero)
            return Hash256.Zero;

        return new Hash256(EtchashMiningHelper.ComputeTargetBytes((BigInteger)difficulty));
    }

    private uint GetEtchashEpoch(long blockNumber) =>
        EtchashMiningHelper.GetEtchashEpoch(blockNumber, _ecip1099Transition, _transitionEpoch);

    private void CleanupOldWork()
    {
        while (_recentWork.Count > MaxRecentWorkItems)
        {
            // Remove oldest entry (simple strategy: remove first found)
            foreach (var key in _recentWork.Keys)
            {
                if (_recentWork.TryRemove(key, out _))
                    break;
            }
        }
    }
}
