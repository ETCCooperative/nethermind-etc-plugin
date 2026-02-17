// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Represents mining work for external miners.
/// </summary>
/// <param name="PowHash">Hash of the block header for PoW computation (without nonce/mixHash).</param>
/// <param name="SeedHash">Seed hash for DAG generation.</param>
/// <param name="Target">Mining target (2^256 / difficulty).</param>
/// <param name="BlockNumber">Block number being mined.</param>
public sealed record MiningWork(
    Hash256 PowHash,
    Hash256 SeedHash,
    Hash256 Target,
    long BlockNumber);
