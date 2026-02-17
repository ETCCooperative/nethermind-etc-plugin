// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.JsonRpc;
using Nethermind.Logging;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Implementation of eth_getWork and eth_submitWork for Ethereum Classic.
/// </summary>
internal sealed class EtcMiningRpcModule : IEtcMiningRpcModule
{
    private readonly IRemoteSealerClient _sealerClient;
    private readonly ILogger _logger;

    public EtcMiningRpcModule(IRemoteSealerClient sealerClient, ILogManager logManager)
    {
        _sealerClient = sealerClient;
        _logger = logManager.GetClassLogger();
    }

    public ResultWrapper<string[]> eth_getWork()
    {
        MiningWork? work = _sealerClient.GetWork();
        if (work is null)
        {
            return ResultWrapper<string[]>.Fail("No mining work available", ErrorCodes.ResourceUnavailable);
        }

        // Return [pow-hash, seed-hash, target, block-number]
        string[] result =
        [
            work.PowHash.ToString(),
            work.SeedHash.ToString(),
            work.Target.ToString(),
            work.BlockNumber.ToHexString(skipLeadingZeros: false)
        ];

        return ResultWrapper<string[]>.Success(result);
    }

    public ResultWrapper<bool> eth_submitWork(byte[] nonce, byte[] powHash, byte[] mixDigest)
    {
        // Validate parameters
        if (nonce is null || nonce.Length != 8)
        {
            return ResultWrapper<bool>.Fail("Invalid nonce: must be 8 bytes", ErrorCodes.InvalidParams);
        }

        if (powHash is null || powHash.Length != 32)
        {
            return ResultWrapper<bool>.Fail("Invalid powHash: must be 32 bytes", ErrorCodes.InvalidParams);
        }

        if (mixDigest is null || mixDigest.Length != 32)
        {
            return ResultWrapper<bool>.Fail("Invalid mixDigest: must be 32 bytes", ErrorCodes.InvalidParams);
        }

        // Convert nonce from big-endian bytes to ulong
        ulong nonceValue = BinaryPrimitives.ReadUInt64BigEndian(nonce);

        var solution = new PoWSolution(
            nonceValue,
            new Hash256(powHash),
            new Hash256(mixDigest));

        bool accepted = _sealerClient.SubmitWork(solution);

        if (_logger.IsDebug)
        {
            _logger.Debug($"eth_submitWork: nonce={nonceValue:X16}, powHash={solution.PowHash}, accepted={accepted}");
        }

        return ResultWrapper<bool>.Success(accepted);
    }
}
