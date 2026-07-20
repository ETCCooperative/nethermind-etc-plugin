// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.EthereumClassic.Config;

/// <summary>
/// Mining mode for Ethereum Classic. Each mode is a valid combination of a sealer
/// (CPU vs remote miners) and a trigger policy (self-triggering vs manual
/// <c>evm_mine</c>); the remaining combination — remote sealing with a manual
/// trigger — would hand external miners a single never-refreshed template, so it
/// is deliberately not expressible.
/// </summary>
public enum EtcMiningMode
{
    /// <summary>
    /// Mining disabled (default).
    /// </summary>
    None,

    /// <summary>
    /// Remote mining: block templates are built continuously and served to external
    /// miners via eth_getWork/eth_submitWork.
    /// </summary>
    Remote,

    /// <summary>
    /// Continuous local CPU mining.
    /// </summary>
    Local,

    /// <summary>
    /// CPU sealing triggered only by evm_mine, for dev/test chains that need
    /// deterministic block heights.
    /// </summary>
    Manual
}
