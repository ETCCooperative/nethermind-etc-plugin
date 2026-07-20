// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.EthereumClassic.Config;

/// <summary>
/// Mining mode for Ethereum Classic.
/// </summary>
public enum EtcMiningMode
{
    /// <summary>
    /// Mining disabled (default).
    /// </summary>
    None,

    /// <summary>
    /// Remote mining via eth_getWork/eth_submitWork.
    /// </summary>
    Remote,

    /// <summary>
    /// CPU sealing triggered only by evm_mine, for dev/test chains that need
    /// deterministic block heights.
    /// </summary>
    Manual
}
