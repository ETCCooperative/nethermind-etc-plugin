// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Interface for managing remote mining work and solutions.
/// </summary>
public interface IRemoteSealerClient
{
    /// <summary>
    /// Gets the current mining work for external miners.
    /// </summary>
    /// <returns>The current mining work, or null if no work is available.</returns>
    MiningWork? GetWork();

    /// <summary>
    /// Submits a PoW solution from an external miner.
    /// </summary>
    /// <param name="solution">The solution to validate and apply.</param>
    /// <returns>True if the solution was valid and accepted.</returns>
    bool SubmitWork(PoWSolution solution);

    /// <summary>
    /// Submits a new block for external miners to work on.
    /// </summary>
    /// <param name="block">The block to mine.</param>
    void SubmitNewWork(Block block);

    /// <summary>
    /// Sets the callback to invoke when a block is successfully mined.
    /// </summary>
    /// <param name="callback">The callback that receives the sealed block.</param>
    void SetOnBlockMined(Action<Block> callback);
}
