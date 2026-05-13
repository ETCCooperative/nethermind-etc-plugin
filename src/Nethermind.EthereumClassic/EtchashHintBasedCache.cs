// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nethermind.EthereumClassic;

internal sealed class EtchashHintBasedCache(Func<EtchashCacheEpoch, IEthashDataSet> createDataSet) : IDisposable
{
    internal const int MaxHintEpochs = 11;
    private static readonly TimeSpan RecentRetention = TimeSpan.FromSeconds(30);

    private readonly Dictionary<Guid, HashSet<uint>> _epochsPerGuid = new();
    private readonly Dictionary<uint, int> _epochRefs = new();
    private readonly Dictionary<uint, Task<IEthashDataSet>> _cachedSets = new();
    private readonly Dictionary<uint, DataSetWithTime> _recent = new();
    private readonly Func<EtchashCacheEpoch, IEthashDataSet> _createDataSet = createDataSet;
    private readonly Lock _lock = new();

    private int _cachedEpochsCount;

    public int CachedEpochsCount
    {
        get { lock (_lock) { return _cachedEpochsCount; } }
    }

    public void Hint(Guid guid, IReadOnlyList<EtchashCacheEpoch> epochs)
    {
        if (epochs.Count > MaxHintEpochs)
            throw new InvalidOperationException("Hint too wide");

        lock (_lock)
        {
            if (!_epochsPerGuid.TryGetValue(guid, out HashSet<uint>? epochsForGuid))
            {
                epochsForGuid = [];
                _epochsPerGuid[guid] = epochsForGuid;
            }

            foreach (uint oldSeedEpoch in epochsForGuid.ToArray())
            {
                if (ContainsSeedEpoch(epochs, oldSeedEpoch))
                    continue;

                epochsForGuid.Remove(oldSeedEpoch);
                DecrementRef(oldSeedEpoch);
            }

            foreach (EtchashCacheEpoch epoch in epochs)
            {
                if (!epochsForGuid.Add(epoch.SeedEpoch))
                    continue;

                IncrementRef(epoch);
            }
        }
    }

    /// <summary>
    /// Returns the in-flight or completed build task for the given epoch, or <c>null</c>
    /// if the epoch is not currently tracked by any hinter.
    /// </summary>
    public Task<IEthashDataSet>? GetTask(EtchashCacheEpoch epoch)
    {
        lock (_lock)
        {
            return _cachedSets.TryGetValue(epoch.SeedEpoch, out Task<IEthashDataSet>? dataSetTask)
                ? dataSetTask
                : null;
        }
    }

    /// <summary>
    /// Convenience wrapper over <see cref="GetTask"/> that blocks the caller until the dataset
    /// build finishes. Building a fresh DAG cache takes seconds for higher epochs, so callers
    /// with no fallback path (e.g. consensus validation) accept that cost on the first call
    /// after an epoch boundary; callers that can defer should use <see cref="GetTask"/> instead.
    /// </summary>
    public IEthashDataSet? Get(EtchashCacheEpoch epoch) => GetTask(epoch)?.Result;

    public void Dispose()
    {
        Task<IEthashDataSet>[] cachedSets;
        DataSetWithTime[] recentSets;

        lock (_lock)
        {
            cachedSets = [.. _cachedSets.Values];
            recentSets = [.. _recent.Values];

            _epochsPerGuid.Clear();
            _epochRefs.Clear();
            _cachedSets.Clear();
            _recent.Clear();
            _cachedEpochsCount = 0;
        }

        foreach (Task<IEthashDataSet> dataSet in cachedSets)
        {
            DisposeDataSet(dataSet);
        }

        foreach (DataSetWithTime recent in recentSets)
        {
            DisposeDataSet(recent.DataSet);
        }
    }

    private static bool ContainsSeedEpoch(IReadOnlyList<EtchashCacheEpoch> epochs, uint seedEpoch)
    {
        for (int i = 0; i < epochs.Count; i++)
        {
            if (epochs[i].SeedEpoch == seedEpoch)
                return true;
        }

        return false;
    }

    private void IncrementRef(EtchashCacheEpoch epoch)
    {
        _epochRefs.TryGetValue(epoch.SeedEpoch, out int refCount);
        _epochRefs[epoch.SeedEpoch] = refCount + 1;

        if (refCount != 0)
            return;

        if (_recent.Remove(epoch.SeedEpoch, out DataSetWithTime reused))
        {
            _cachedSets[epoch.SeedEpoch] = reused.DataSet;
        }
        else
        {
            PruneRecent();
            _cachedSets[epoch.SeedEpoch] = Task.Run(() => _createDataSet(epoch));
        }

        _cachedEpochsCount++;
    }

    private void DecrementRef(uint seedEpoch)
    {
        if (!_epochRefs.TryGetValue(seedEpoch, out int refCount))
            throw new InvalidOperationException("Epoch ref missing");

        refCount--;
        if (refCount < 0)
            throw new InvalidOperationException("Epoch ref below zero");

        if (refCount > 0)
        {
            _epochRefs[seedEpoch] = refCount;
            return;
        }

        _epochRefs.Remove(seedEpoch);
        if (_cachedSets.Remove(seedEpoch, out Task<IEthashDataSet>? removed))
        {
            _recent[seedEpoch] = new DataSetWithTime(DateTimeOffset.UtcNow, removed);
            _cachedEpochsCount--;
        }
    }

    private void PruneRecent()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - RecentRetention;
        foreach ((uint seedEpoch, DataSetWithTime recent) in _recent.ToArray())
        {
            if (recent.Timestamp >= cutoff)
                continue;

            _recent.Remove(seedEpoch);
            DisposeDataSet(recent.DataSet);
        }
    }

    private static void DisposeDataSet(Task<IEthashDataSet> dataSetTask)
    {
        if (dataSetTask.IsCompletedSuccessfully)
        {
            dataSetTask.Result.Dispose();
            return;
        }

        dataSetTask.ContinueWith(
            static task =>
            {
                if (task.IsCompletedSuccessfully)
                    task.Result.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly struct DataSetWithTime(DateTimeOffset timestamp, Task<IEthashDataSet> dataSet)
    {
        public DateTimeOffset Timestamp { get; } = timestamp;
        public Task<IEthashDataSet> DataSet { get; } = dataSet;
    }
}
