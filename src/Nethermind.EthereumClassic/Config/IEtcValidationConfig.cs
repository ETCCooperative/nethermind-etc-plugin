// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Config;

namespace Nethermind.EthereumClassic.Config;

public interface IEtcValidationConfig : IConfig
{
    [ConfigItem(
        Description = "Re-verify the Etchash proof-of-work seal of every block on the intake path. By default " +
                      "full sync and the Era1 importer re-execute transactions and validate state/receipts " +
                      "roots but never check the PoW seal — so an Era import trusts the seal. Enable this to " +
                      "turn processing into a full independent re-validation (state + PoW), e.g. for consensus " +
                      "replay. Significantly slower: it builds the Etchash DAG and hashes every header. Note " +
                      "this also forces a full PoW check on every body downloaded during a fast/snap sync, " +
                      "which makes that sync mode dramatically slower.",
        DefaultValue = "false")]
    bool ForceSealCheck { get; set; }
}
