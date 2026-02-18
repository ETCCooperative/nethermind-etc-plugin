// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0
//
// Background monitor that enables/disables MESS based on peer count and head freshness.
// Mirrors go-ethereum's ecbp1100 activation logic.

using System;
using System.Threading;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Synchronization.Peers;

namespace Nethermind.EthereumClassic;

internal sealed class MessActivationMonitor : IDisposable
{
    // Check interval: 30 * 13s block time = 390s (~6.5 minutes), same as go-ethereum.
    private const int CheckIntervalMs = 390_000;

    // Minimum peers to consider the node well-connected.
    private const int MinPeers = 5;

    // Maximum age of head block to consider it fresh (same as check interval).
    private const ulong MaxHeadAgeSec = 390;

    private readonly EtcBlockTree _blockTree;
    private readonly ISyncPeerPool _syncPeerPool;
    private readonly ITimestamper _timestamper;
    private readonly ILogger _logger;
    private Timer? _timer;

    public MessActivationMonitor(
        EtcBlockTree blockTree,
        ISyncPeerPool syncPeerPool,
        ITimestamper timestamper,
        ILogManager logManager)
    {
        _blockTree = blockTree;
        _syncPeerPool = syncPeerPool;
        _timestamper = timestamper;
        _logger = logManager.GetClassLogger<MessActivationMonitor>();
    }

    public void Start()
    {
        _timer = new Timer(_ => Check(), null, CheckIntervalMs, CheckIntervalMs);
        if (_logger.IsInfo) _logger.Info("MESS activation monitor started");
    }

    private void Check()
    {
        bool wasPreviouslyEnabled = _blockTree.IsMessEnabled;

        int peerCount = _syncPeerPool.PeerCount;
        bool enoughPeers = peerCount >= MinPeers;

        BlockHeader? head = _blockTree.Head?.Header;
        bool headFresh = false;
        if (head is not null)
        {
            ulong now = _timestamper.UnixTime.Seconds;
            headFresh = now - head.Timestamp < MaxHeadAgeSec;
        }

        bool shouldEnable = enoughPeers && headFresh;
        _blockTree.EnableMess(shouldEnable);

        if (shouldEnable != wasPreviouslyEnabled)
        {
            if (_logger.IsInfo) _logger.Info(
                $"MESS {(shouldEnable ? "activated" : "deactivated")}: " +
                $"peers={peerCount}, headFresh={headFresh}");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
