// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

/// <summary>
/// Unit tests for ECIP-1017 monetary policy.
/// </summary>
[TestFixture]
public class Ecip1017CalculatorTests
{
    private const long MainnetEra = 5_000_000;
    private const long MordorEra = 2_000_000;

    // Block reward boundaries: (blockNumber, eraPeriod, expectedWei)
    private static readonly object[] BlockRewardCases =
    [
        // Mainnet era boundaries
        new object[] { 1L, MainnetEra, 5_000_000_000_000_000_000UL },           // Era 1
        new object[] { 5_000_001L, MainnetEra, 4_000_000_000_000_000_000UL },   // Era 2
        new object[] { 10_000_001L, MainnetEra, 3_200_000_000_000_000_000UL },  // Era 3
        new object[] { 15_000_001L, MainnetEra, 2_560_000_000_000_000_000UL },  // Era 4
        new object[] { 20_000_001L, MainnetEra, 2_048_000_000_000_000_000UL },  // Era 5
        // Mordor (2M era)
        new object[] { 2_000_001L, MordorEra, 4_000_000_000_000_000_000UL },
    ];

    [TestCaseSource(nameof(BlockRewardCases))]
    public void CalculateBlockReward_Returns_Expected_Value(long blockNumber, long eraPeriod, ulong expectedWei)
    {
        var reward = Ecip1017Calculator.CalculateBlockReward(blockNumber, eraPeriod);
        ((ulong)reward).Should().Be(expectedWei);
    }

    [Test]
    public void CalculateBlockReward_Era_Reduction_Is_20_Percent()
    {
        var era1 = Ecip1017Calculator.CalculateBlockReward(1, MainnetEra);
        var era2 = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);
        era2.Should().Be(era1 * 4 / 5);
    }

    [Test]
    public void GetEra_Boundaries()
    {
        Ecip1017Calculator.GetEra(5_000_000, MainnetEra).Should().Be(0);  // Last of Era 1
        Ecip1017Calculator.GetEra(5_000_001, MainnetEra).Should().Be(1);  // First of Era 2
        Ecip1017Calculator.GetEra(2_000_001, MordorEra).Should().Be(1);   // Mordor Era 2
    }

    [Test]
    public void CalculateUncleReward_Era0_Decreases_With_Distance()
    {
        var blockReward = Ecip1017Calculator.CalculateBlockReward(100, MainnetEra);

        // Era 0: reward = blockReward * (8 - distance) / 8
        var dist1 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 99, 0);
        var dist6 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 94, 0);

        dist1.Should().Be(blockReward * 7 / 8);
        dist6.Should().Be(blockReward * 2 / 8);
    }

    [Test]
    public void CalculateUncleReward_Era1Plus_Is_Fixed_OneThirtySecond()
    {
        var blockReward = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);

        // Era 1+: fixed 1/32 regardless of distance
        var reward = Ecip1017Calculator.CalculateUncleReward(blockReward, 5_000_100, 5_000_099, 1);
        reward.Should().Be(blockReward >> 5);
    }
}
