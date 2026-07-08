// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using FluentAssertions;
using Nethermind.EthereumClassic.Mining;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test.Mining;

[Parallelizable(ParallelScope.All)]
[TestFixture]
public class MiningHashrateTrackerTests
{
    private const long Ttl = MiningHashrateTracker.HashrateTtlMs;

    [Test]
    public void GetTotal_with_no_submissions_is_zero()
    {
        var tracker = new MiningHashrateTracker();
        tracker.GetTotal(0).Should().Be(UInt256.Zero);
    }

    [Test]
    public void GetTotal_sums_rates_across_ids()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(100), 0);
        tracker.Submit("b", new UInt256(200), 0);

        tracker.GetTotal(0).Should().Be(new UInt256(300));
    }

    [Test]
    public void Submit_same_id_overwrites_previous_rate()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(100), 0);
        tracker.Submit("a", new UInt256(250), 1000);

        // 250, not 350 — a miner reporting again replaces its previous value.
        tracker.GetTotal(1000).Should().Be(new UInt256(250));
    }

    [Test]
    public void Rate_is_still_counted_at_exactly_the_ttl_boundary()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(100), 0);

        tracker.GetTotal(Ttl).Should().Be(new UInt256(100));
    }

    [Test]
    public void Rate_expires_after_the_ttl()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(100), 0);

        tracker.GetTotal(Ttl + 1).Should().Be(UInt256.Zero);
    }

    [Test]
    public void Expired_rate_does_not_affect_a_fresh_submission()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(100), 0);

        // "a" has expired by now; a new report from "b" is the only live entry.
        tracker.Submit("b", new UInt256(500), Ttl + 1);
        tracker.GetTotal(Ttl + 1).Should().Be(new UInt256(500));
    }

    [Test]
    public void Only_non_expired_rates_are_summed()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("stale", new UInt256(100), 0);
        tracker.Submit("fresh", new UInt256(300), Ttl);

        // At Ttl + 1: "stale" (age Ttl+1) is gone, "fresh" (age 1) remains.
        tracker.GetTotal(Ttl + 1).Should().Be(new UInt256(300));
    }

    [Test]
    public void GetTotal_does_not_overflow_ulong()
    {
        var tracker = new MiningHashrateTracker();
        tracker.Submit("a", new UInt256(ulong.MaxValue), 0);
        tracker.Submit("b", new UInt256(ulong.MaxValue), 0);

        UInt256 total = tracker.GetTotal(0);

        // 2 * (2^64 - 1) = 2^65 - 2, which must not wrap around a ulong.
        total.Should().Be(new UInt256(0xFFFF_FFFF_FFFF_FFFEUL, 1UL));
        (total > new UInt256(ulong.MaxValue)).Should().BeTrue();
    }
}
