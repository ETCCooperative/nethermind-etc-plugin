// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Interface for Ethash data set implementations.
/// </summary>
internal interface IEthashDataSet : IDisposable
{
    uint Size { get; }
    uint[] CalcDataSetItem(uint i);
}
