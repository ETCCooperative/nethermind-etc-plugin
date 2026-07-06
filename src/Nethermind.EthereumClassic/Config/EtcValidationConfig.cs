// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

namespace Nethermind.EthereumClassic.Config;

public class EtcValidationConfig : IEtcValidationConfig
{
    public bool ForceSealCheck { get; set; } = false;
}
