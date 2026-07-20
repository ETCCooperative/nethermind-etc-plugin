// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Logging;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Sealer that delegates mining to external miners via eth_getWork/eth_submitWork.
/// </summary>
internal sealed class RemoteEtchashSealer : ISealer
{
    private readonly IRemoteSealerClient _remoteSealerClient;
    private readonly ISigner _signer;
    private readonly IBlockTree _blockTree;
    private readonly ILogger _logger;
    private readonly PendingRemoteSealRegistry<Block> _pending = new();

    public RemoteEtchashSealer(IRemoteSealerClient remoteSealerClient, ISigner signer, IBlockTree blockTree, ILogManager logManager)
    {
        _remoteSealerClient = remoteSealerClient;
        _signer = signer;
        _blockTree = blockTree;
        _logger = logManager.GetClassLogger<RemoteEtchashSealer>();
        _remoteSealerClient.SetOnBlockMined(OnBlockMined);
    }

    public Address Address => _signer.Address;

    public bool CanSeal(long blockNumber, Hash256 parentHash) => true;

    public Task<Block> SealBlock(Block block, CancellationToken cancellationToken)
    {
        ValidateBlock(block);

        PendingRemoteSeal<Block> pendingSeal = _pending.Replace(block, cancellationToken);
        _remoteSealerClient.SubmitNewWork(block);

        if (_logger.IsInfo) _logger.Info($"Submitted block {block.Number} for remote mining");

        return pendingSeal.Task;
    }

    /// <summary>
    /// A solution for superseded work (its template was replaced by a work refresh or a
    /// competing <c>SealBlock</c> call) no longer completes the pending seal, but if it
    /// still extends the current head it is a perfectly valid block, so it is suggested
    /// to the block tree directly — mirroring geth's remote sealer, which accepts
    /// solutions for stale work packages instead of discarding the miner's effort.
    /// </summary>
    private void OnBlockMined(Block sealedBlock)
    {
        if (_pending.TryCompleteActive(sealedBlock))
            return;

        if (sealedBlock.ParentHash == _blockTree.Head?.Hash)
        {
            if (_logger.IsInfo) _logger.Info($"Remote mining result for superseded work still extends the head, suggesting block {sealedBlock.Number}");
            _blockTree.SuggestBlock(sealedBlock);
        }
        else if (_logger.IsDebug)
        {
            _logger.Debug($"Ignoring stale remote mining result for block {sealedBlock.Number}");
        }
    }

    private static void ValidateBlock(Block block)
    {
        if (block.Header.TxRoot is null ||
            block.Header.StateRoot is null ||
            block.Header.ReceiptsRoot is null ||
            block.Header.UnclesHash is null ||
            block.Header.Bloom is null ||
            block.Header.ExtraData is null)
        {
            throw new InvalidOperationException($"Requested to mine an invalid block {block.Header}");
        }
    }
}
