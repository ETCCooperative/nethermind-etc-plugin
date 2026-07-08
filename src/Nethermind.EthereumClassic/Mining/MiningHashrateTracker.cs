// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Int256;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Tracks the hashrates reported by external miners via <c>eth_submitHashrate</c> and
/// aggregates them for <c>eth_hashrate</c>.
///
/// Mirrors go-ethereum's remote sealer (consensus/ethash/sealer.go): each miner <c>id</c>
/// maps to a single (rate, timestamp) entry, a new report overwrites the previous one for
/// that id, and an entry stops being counted <see cref="HashrateTtlMs"/> ms after its last
/// report. Instead of go-ethereum's background ticker, expired entries are pruned lazily on
/// each access, which keeps this class free of any timer/thread.
///
/// Pure logic (no Nethermind runtime types) so it is unit-testable against the reference
/// assemblies; the caller supplies the current time in milliseconds.
/// </summary>
internal sealed class MiningHashrateTracker
{
    /// <summary>
    /// How long a reported hashrate stays counted after its last submission. Matches
    /// go-ethereum's 10-second expiry (<c>time.Since(rate.ping) &gt; 10*time.Second</c>).
    /// </summary>
    internal const long HashrateTtlMs = 10_000;

    private readonly Dictionary<string, Entry> _rates = new();
    private readonly object _lock = new();

    /// <summary>Records (or overwrites) the hashrate reported by a miner.</summary>
    /// <param name="id">Opaque miner/client identifier.</param>
    /// <param name="rate">Reported hashrate in hashes per second.</param>
    /// <param name="nowMs">Current time in Unix milliseconds.</param>
    public void Submit(string id, UInt256 rate, long nowMs)
    {
        lock (_lock)
        {
            _rates[id] = new Entry(rate, nowMs);
            Prune(nowMs);
        }
    }

    /// <summary>
    /// Returns the sum of all non-expired reported hashrates, pruning expired entries.
    /// </summary>
    /// <param name="nowMs">Current time in Unix milliseconds.</param>
    public UInt256 GetTotal(long nowMs)
    {
        lock (_lock)
        {
            Prune(nowMs);

            UInt256 total = UInt256.Zero;
            foreach (Entry entry in _rates.Values)
            {
                total += entry.Rate;
            }

            return total;
        }
    }

    private void Prune(long nowMs)
    {
        // Collect expired keys first to avoid mutating the dictionary while enumerating it.
        List<string>? expired = null;
        foreach (KeyValuePair<string, Entry> kvp in _rates)
        {
            if (nowMs - kvp.Value.TimestampMs > HashrateTtlMs)
            {
                (expired ??= new List<string>()).Add(kvp.Key);
            }
        }

        if (expired is not null)
        {
            foreach (string key in expired)
            {
                _rates.Remove(key);
            }
        }
    }

    private readonly record struct Entry(UInt256 Rate, long TimestampMs);
}
