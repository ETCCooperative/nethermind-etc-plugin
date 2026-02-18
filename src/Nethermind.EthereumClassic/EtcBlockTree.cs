// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0
//
// EtcBlockTree overrides HeadImprovementRequirementsSatisfied to apply
// MESS (ECBP-1100) antigravity scoring on reorg attempts.

using Autofac.Features.AttributeFilters;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Blocks;
using Nethermind.Blockchain.Headers;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Db;
using Nethermind.Db.Blooms;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.State.Repositories;

namespace Nethermind.EthereumClassic;

internal class EtcBlockTree : BlockTree
{
    private volatile bool _messEnabled;

    public EtcBlockTree(
        IBlockStore? blockStore,
        IHeaderStore? headerDb,
        [KeyFilter("blockInfos")] IDb? blockInfoDb,
        [KeyFilter("metadata")] IDb? metadataDb,
        IBadBlockStore? badBlockStore,
        IChainLevelInfoRepository? chainLevelInfoRepository,
        ISpecProvider? specProvider,
        IBloomStorage? bloomStorage,
        ISyncConfig? syncConfig,
        ILogManager? logManager,
        long genesisBlockNumber = 0)
        : base(blockStore, headerDb, blockInfoDb, metadataDb, badBlockStore,
            chainLevelInfoRepository, specProvider, bloomStorage, syncConfig,
            logManager, genesisBlockNumber)
    {
    }

    public void EnableMess(bool enable) => _messEnabled = enable;

    public bool IsMessEnabled => _messEnabled;

    protected override bool HeadImprovementRequirementsSatisfied(BlockHeader header)
    {
        if (!base.HeadImprovementRequirementsSatisfied(header))
            return false;

        if (!_messEnabled)
            return true;

        BlockHeader? currentHead = Head?.Header;
        if (currentHead is null)
            return true;

        BlockHeader? ancestor = FindCommonAncestor(currentHead, header);
        if (ancestor is null)
            return true;

        // Chain extension (proposed builds directly on head) — never penalize.
        if (ancestor.Hash == currentHead.Hash)
            return true;

        UInt256 commonAncestorTD = ancestor.TotalDifficulty ?? UInt256.Zero;
        UInt256 localTD = currentHead.TotalDifficulty ?? UInt256.Zero;
        UInt256 proposedTD = header.TotalDifficulty ?? UInt256.Zero;

        if (MessCalculator.ShouldRejectReorg(
                commonAncestorTD,
                localTD,
                proposedTD,
                ancestor.Timestamp,
                currentHead.Timestamp))
        {
            if (Logger.IsInfo) Logger.Info(
                $"MESS rejected reorg: ancestor #{ancestor.Number} ({ancestor.Hash}), " +
                $"head #{currentHead.Number}, proposed #{header.Number} ({header.Hash}), " +
                $"timeDelta={currentHead.Timestamp - ancestor.Timestamp}s");
            return false;
        }

        return true;
    }

    private BlockHeader? FindCommonAncestor(BlockHeader a, BlockHeader b)
    {
        const int maxDepth = 8192;
        int steps = 0;

        // Walk both chains back to the same height, then walk together until hashes match.
        BlockHeader? ha = a;
        BlockHeader? hb = b;

        while (ha is not null && hb is not null && ha.Number > hb.Number && steps < maxDepth)
        {
            ha = FindHeader(ha.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        while (hb is not null && ha is not null && hb.Number > ha.Number && steps < maxDepth)
        {
            hb = FindHeader(hb.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        while (ha is not null && hb is not null && ha.Hash != hb.Hash && steps < maxDepth)
        {
            ha = FindHeader(ha.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            hb = FindHeader(hb.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        if (ha is not null && hb is not null && ha.Hash == hb.Hash)
            return ha;

        return null;
    }
}
