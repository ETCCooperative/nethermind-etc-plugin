// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Represents a Proof of Work solution submitted by an external miner.
/// </summary>
/// <param name="Nonce">The nonce that solves the PoW puzzle.</param>
/// <param name="PowHash">The pow-hash identifying which block this solution is for.</param>
/// <param name="MixHash">The mix digest proving correct computation.</param>
public sealed record PoWSolution(
    ulong Nonce,
    Hash256 PowHash,
    Hash256 MixHash);
