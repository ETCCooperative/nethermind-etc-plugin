// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

[TestFixture]
public class MessCalculatorTests
{
    [Test]
    public void PolynomialV_AtZero_Returns128()
    {
        UInt256 result = MessCalculator.PolynomialV(0);
        result.Should().Be((UInt256)128);
    }

    [Test]
    public void PolynomialV_AtXCap_Returns3968()
    {
        // At xcap=25132: denominator(128) + height(3840) = 3968
        UInt256 result = MessCalculator.PolynomialV(25132);
        result.Should().Be((UInt256)3968);
    }

    [Test]
    public void PolynomialV_BeyondXCap_CapsAt3968()
    {
        UInt256 result = MessCalculator.PolynomialV(100_000);
        result.Should().Be((UInt256)3968);
    }

    [Test]
    public void PolynomialV_MonotonicallyIncreasing()
    {
        UInt256 prev = MessCalculator.PolynomialV(0);
        // Sample at intervals across the range
        for (ulong t = 100; t <= 25132; t += 100)
        {
            UInt256 current = MessCalculator.PolynomialV(t);
            current.Should().BeGreaterThanOrEqualTo(prev,
                $"PolynomialV should be monotonically increasing at t={t}");
            prev = current;
        }
    }

    [Test]
    public void PolynomialV_AtMidpoint_ReturnsExpectedValue()
    {
        // At x=12566 (half of xcap), the S-curve should be at roughly half height.
        // Exact: 128 + (3*12566^2 - 2*12566^3/25132) * 3840 / 25132^2
        // = 128 + (3*157904356 - 2*1984241013496/25132) * 3840 / 631617424
        // = 128 + (473713068 - 157904356) * 3840 / 631617424
        // = 128 + 315808712 * 3840 / 631617424
        // = 128 + 1920 = 2048
        UInt256 result = MessCalculator.PolynomialV(12566);
        result.Should().Be((UInt256)2048);
    }

    [Test]
    public void ShouldRejectReorg_DeepFork_Rejected()
    {
        // Common ancestor 7200s ago (~2 hours), proposed has barely more TD than local.
        // antigravity at 7200s should be significant, requiring much more TD.
        UInt256 commonAncestorTD = 1_000_000;
        UInt256 localTD = 1_100_000; // local gained 100k TD since ancestor
        UInt256 proposedTD = 1_100_001; // proposed gained 100k+1 TD — barely more
        ulong commonAncestorTime = 1_000_000;
        ulong currentHeadTime = 1_007_200; // 7200s later

        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD, localTD, proposedTD,
            commonAncestorTime, currentHeadTime);

        rejected.Should().BeTrue("a deep fork with barely more TD should be rejected");
    }

    [Test]
    public void ShouldRejectReorg_ShallowFork_Accepted()
    {
        // Common ancestor just 13s ago (1 block), proposed has more TD.
        // antigravity at 13s ≈ 128 (almost no penalty).
        UInt256 commonAncestorTD = 1_000_000;
        UInt256 localTD = 1_000_100;
        UInt256 proposedTD = 1_000_200; // clearly more TD
        ulong commonAncestorTime = 1_000_000;
        ulong currentHeadTime = 1_000_013; // 13s later

        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD, localTD, proposedTD,
            commonAncestorTime, currentHeadTime);

        rejected.Should().BeFalse("a shallow fork with more TD should be accepted");
    }

    [Test]
    public void ShouldRejectReorg_ChainExtension_NeverRejected()
    {
        // Chain extension: proposed builds on current head, so commonAncestor == head.
        // localSubchainTD = 0, so antigravity * 0 = 0 and proposedSubchainTD * 128 > 0.
        UInt256 commonAncestorTD = 1_000_000;
        UInt256 localTD = 1_000_000; // head IS the common ancestor
        UInt256 proposedTD = 1_000_100;
        ulong commonAncestorTime = 1_000_000;
        ulong currentHeadTime = 1_000_000;

        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD, localTD, proposedTD,
            commonAncestorTime, currentHeadTime);

        rejected.Should().BeFalse("chain extension should never be rejected");
    }

    [Test]
    public void ShouldRejectReorg_EqualTD_Rejected()
    {
        // Equal TD with time delta > 0 means antigravity > denominator, so rejection.
        UInt256 commonAncestorTD = 1_000_000;
        UInt256 localTD = 1_100_000;
        UInt256 proposedTD = 1_100_000; // same TD
        ulong commonAncestorTime = 1_000_000;
        ulong currentHeadTime = 1_001_000; // 1000s later

        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD, localTD, proposedTD,
            commonAncestorTime, currentHeadTime);

        rejected.Should().BeTrue("equal TD with time delta should be rejected");
    }

    [Test]
    public void ShouldRejectReorg_MassivelyHigherTD_Accepted()
    {
        // Even at max antigravity (31:1 ratio), if proposed has 31x+ more subchain TD, accept.
        UInt256 commonAncestorTD = 1_000_000;
        UInt256 localTD = 1_100_000; // +100k subchain TD
        UInt256 proposedTD = 4_200_000; // +3.2M subchain TD (32x more)
        ulong commonAncestorTime = 1_000_000;
        ulong currentHeadTime = 1_030_000; // 30000s (well past xcap)

        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD, localTD, proposedTD,
            commonAncestorTime, currentHeadTime);

        rejected.Should().BeFalse("massively higher TD should overcome antigravity");
    }
}
