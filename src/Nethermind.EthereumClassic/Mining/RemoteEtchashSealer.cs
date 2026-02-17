// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ILogger _logger;

    public RemoteEtchashSealer(IRemoteSealerClient remoteSealerClient, ISigner signer, ILogManager logManager)
    {
        _remoteSealerClient = remoteSealerClient;
        _signer = signer;
        _logger = logManager.GetClassLogger();
    }

    public Address Address => _signer.Address;

    public bool CanSeal(long blockNumber, Hash256 parentHash) => true;

    public Task<Block> SealBlock(Block block, CancellationToken cancellationToken)
    {
        ValidateBlock(block);

        var tcs = new TaskCompletionSource<Block>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register cancellation
        var registration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled(cancellationToken);
        });

        // Set callback for when block is mined
        _remoteSealerClient.SetOnBlockMined(sealedBlock =>
        {
            registration.Dispose();
            tcs.TrySetResult(sealedBlock);
        });

        // Submit work to remote miners
        _remoteSealerClient.SubmitNewWork(block);

        if (_logger.IsInfo) _logger.Info($"Submitted block {block.Number} for remote mining");

        return tcs.Task;
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
