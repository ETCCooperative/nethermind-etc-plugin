// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Consensus;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Logging;
using Nethermind.TxPool;

namespace Nethermind.EthereumClassic;

/// <summary>
/// A <see cref="BlockValidator"/> that additionally re-verifies the Etchash proof-of-work seal of every
/// block it validates. The normal processing path (full sync, the Era1 importer, and the fast-blocks bodies
/// download) only runs the cheap <c>ISealValidator.ValidateParams</c> (difficulty/timestamp) via
/// <c>HeaderValidator</c>; the expensive <c>ValidateSeal</c> PoW check is called only by the gossip/sync-server
/// path, so an Era import re-executes state but trusts the seal. Registered as <see cref="IBlockValidator"/>
/// only when <c>EtcValidation.ForceSealCheck</c> is set, turning the Era import into a full independent
/// re-validation (state + PoW) — at the cost of building the Etchash DAG.
/// </summary>
/// <remarks>
/// <see cref="ValidateBodyAgainstHeader(BlockHeader, BlockBody, out string?)"/> is called by every block/body
/// intake path — the Era1 importer, the full-sync <c>BlockDownloader</c>, and the fast-blocks
/// <c>BodiesSyncFeed</c> — so enabling this also forces full PoW verification on every body downloaded during a
/// fast/snap sync, not only during Era import.
/// </remarks>
public class SealValidatingBlockValidator(
    ITxValidator? txValidator,
    IHeaderValidator? headerValidator,
    IUnclesValidator? unclesValidator,
    ISpecProvider? specProvider,
    ISealValidator sealValidator,
    ILogManager? logManager)
    : BlockValidator(txValidator, headerValidator, unclesValidator, specProvider, logManager)
{
    private readonly ISealValidator _sealValidator = sealValidator ?? throw new ArgumentNullException(nameof(sealValidator));
    private readonly ILogger _logger = logManager?.GetClassLogger<SealValidatingBlockValidator>() ?? throw new ArgumentNullException(nameof(logManager));

    // ValidateBodyAgainstHeader is the per-block validator call each intake path makes before queueing a block
    // to the processor (Era1 importer, full-sync BlockDownloader, and fast-blocks BodiesSyncFeed), so it is the
    // reliable hook. force: true makes EthashSealValidator check EVERY block (it otherwise samples ~1 in 1024).
    public override bool ValidateBodyAgainstHeader(BlockHeader header, BlockBody toBeValidated, out string? error)
    {
        if (!_sealValidator.ValidateSeal(header, force: true))
        {
            error = $"Invalid proof-of-work seal for block {header.Number} ({header.Hash})";
            if (_logger.IsWarn) _logger.Warn(error);
            return false;
        }

        return base.ValidateBodyAgainstHeader(header, toBeValidated, out error);
    }
}
