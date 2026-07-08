// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Int256;
using Nethermind.JsonRpc;
using Nethermind.JsonRpc.Modules;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// RPC module for Ethereum Classic mining operations.
/// </summary>
[RpcModule(ModuleType.Eth)]
public interface IEtcMiningRpcModule : IRpcModule
{
    /// <summary>
    /// Returns mining work for external miners.
    /// </summary>
    /// <returns>Array of [pow-hash, seed-hash, target, block-number].</returns>
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Returns mining work for external miners.",
        ExampleResponse = "[\"0x1234...\",\"0x5678...\",\"0xabcd...\",\"0x100\"]")]
    ResultWrapper<string[]> eth_getWork();

    /// <summary>
    /// Submits a mining solution.
    /// </summary>
    /// <param name="nonce">The 8-byte nonce (big-endian).</param>
    /// <param name="powHash">The 32-byte pow-hash identifying the work.</param>
    /// <param name="mixDigest">The 32-byte mix digest proving correct computation.</param>
    /// <returns>True if the solution was accepted.</returns>
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Submits a mining solution.",
        ExampleResponse = "true")]
    ResultWrapper<bool> eth_submitWork(byte[] nonce, byte[] powHash, byte[] mixDigest);

    /// <summary>
    /// Reports the hashrate of an external miner. Accepted for compatibility with the
    /// historical getwork mining protocol; the value is aggregated for <see cref="eth_hashrate"/>.
    /// </summary>
    /// <param name="hashRate">The reported hashrate in hashes per second.</param>
    /// <param name="id">An opaque identifier for the reporting miner.</param>
    /// <returns>True once the hashrate is recorded.</returns>
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Submits the mining hashrate reported by an external miner.",
        ExampleResponse = "true")]
    ResultWrapper<bool> eth_submitHashrate(UInt256 hashRate, string id);

    /// <summary>
    /// Returns the combined hashrate most recently reported by external miners.
    /// </summary>
    /// <returns>The aggregated hashrate in hashes per second.</returns>
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Returns the aggregated hashrate reported by external miners.",
        ExampleResponse = "0x38a")]
    ResultWrapper<UInt256> eth_hashrate();

    /// <summary>
    /// Returns whether the node is currently mining (producing work for external miners).
    /// </summary>
    /// <returns>True while remote mining is active.</returns>
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Returns whether the node is mining.",
        ExampleResponse = "true")]
    ResultWrapper<bool> eth_mining();
}
