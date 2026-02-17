// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Consensus;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Logging;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Sealer that performs local CPU mining using Etchash.
/// </summary>
internal sealed class LocalEtchashSealer : ISealer
{
    private readonly IEthash _ethash;
    private readonly ISigner _signer;
    private readonly ILogger _logger;

    public LocalEtchashSealer(IEthash ethash, ISigner signer, ILogManager logManager)
    {
        _ethash = ethash ?? throw new ArgumentNullException(nameof(ethash));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _logger = logManager?.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));
    }

    public Address Address => _signer.Address;

    public bool CanSeal(long blockNumber, Hash256 parentHash) => true;

    public async Task<Block> SealBlock(Block block, CancellationToken cancellationToken)
    {
        ValidateBlock(block);

        Block? sealedBlock = await Task.Factory.StartNew(() => Mine(block), cancellationToken)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error($"{nameof(SealBlock)} failed", t.Exception);
                    return null;
                }

                if (t.IsCompletedSuccessfully)
                {
                    t.Result.Header.Hash = t.Result.Header.CalculateHash();
                }

                return t.Result;
            }, cancellationToken, TaskContinuationOptions.NotOnFaulted, TaskScheduler.Default);

        return sealedBlock ?? throw new SealEngineException($"{nameof(SealBlock)} failed");
    }

    private Block Mine(Block block)
    {
        if (_logger.IsInfo) _logger.Info($"Starting CPU mining for block {block.Number}");

        (Hash256 mixHash, ulong nonce) = _ethash.Mine(block.Header, startNonce: null);

        block.Header.Nonce = nonce;
        block.Header.MixHash = mixHash;

        if (_logger.IsInfo) _logger.Info($"Mined block {block.Number} with nonce {nonce:X16}");

        return block;
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
