// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Config;

namespace Nethermind.EthereumClassic.Config;

public interface IEtcMessConfig : IConfig
{
    [ConfigItem(
        Description = "Enable MESS (ECBP-1100) artificial finality. When true, deep reorgs are penalized based on time elapsed.",
        DefaultValue = "false")]
    bool Enabled { get; set; }
}
