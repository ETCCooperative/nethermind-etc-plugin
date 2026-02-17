// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.EthereumClassic.Config;

public interface IEtcMiningConfig : IConfig
{
    [ConfigItem(
        Description = "Mining mode: None (disabled), Remote (eth_getWork/submitWork), Local (CPU mining).",
        DefaultValue = "None")]
    EtcMiningMode Mode { get; set; }
}
