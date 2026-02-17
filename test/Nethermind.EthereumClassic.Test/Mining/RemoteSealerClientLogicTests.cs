// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Numerics;
using FluentAssertions;
using Nethermind.EthereumClassic.Mining;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test.Mining;

[Parallelizable(ParallelScope.All)]
[TestFixture]
public class EtchashMiningHelperTests
{
    private const long Ecip1099Transition = 11_700_000;
    private const uint TransitionEpoch = (uint)(Ecip1099Transition / 30_000); // 390

    // --- Epoch calculation tests ---

    [Test]
    public void GetEtchashEpoch_before_transition_uses_30k_epochs()
    {
        EtchashMiningHelper.GetEtchashEpoch(0, Ecip1099Transition, TransitionEpoch).Should().Be(0);
        EtchashMiningHelper.GetEtchashEpoch(29_999, Ecip1099Transition, TransitionEpoch).Should().Be(0);
        EtchashMiningHelper.GetEtchashEpoch(30_000, Ecip1099Transition, TransitionEpoch).Should().Be(1);
        EtchashMiningHelper.GetEtchashEpoch(60_000, Ecip1099Transition, TransitionEpoch).Should().Be(2);
    }

    [Test]
    public void GetEtchashEpoch_at_transition_starts_etchash_epochs()
    {
        uint epoch = EtchashMiningHelper.GetEtchashEpoch(Ecip1099Transition, Ecip1099Transition, TransitionEpoch);
        epoch.Should().Be(TransitionEpoch / 2); // 195
    }

    [Test]
    public void GetEtchashEpoch_after_transition_uses_60k_epochs()
    {
        uint epoch = EtchashMiningHelper.GetEtchashEpoch(Ecip1099Transition + 60_000, Ecip1099Transition, TransitionEpoch);
        epoch.Should().Be(TransitionEpoch / 2 + 1); // 196
    }

    [Test]
    public void GetEtchashEpoch_just_before_transition()
    {
        uint epoch = EtchashMiningHelper.GetEtchashEpoch(Ecip1099Transition - 1, Ecip1099Transition, TransitionEpoch);
        epoch.Should().Be((uint)((Ecip1099Transition - 1) / 30_000)); // 389
    }

    // --- Seed epoch calculation tests ---

    [Test]
    public void GetSeedEpoch_before_ecip1099_returns_dagEpoch_unchanged()
    {
        EtchashMiningHelper.GetSeedEpoch(10, ecip1099Active: false).Should().Be(10);
        EtchashMiningHelper.GetSeedEpoch(0, ecip1099Active: false).Should().Be(0);
        EtchashMiningHelper.GetSeedEpoch(389, ecip1099Active: false).Should().Be(389);
    }

    [Test]
    public void GetSeedEpoch_after_ecip1099_doubles_dagEpoch()
    {
        EtchashMiningHelper.GetSeedEpoch(195, ecip1099Active: true).Should().Be(390);
        EtchashMiningHelper.GetSeedEpoch(196, ecip1099Active: true).Should().Be(392);
        EtchashMiningHelper.GetSeedEpoch(0, ecip1099Active: true).Should().Be(0);
    }

    // --- Target computation tests ---

    [Test]
    public void ComputeTargetBytes_returns_32_bytes()
    {
        byte[] target = EtchashMiningHelper.ComputeTargetBytes(1_000_000);
        target.Should().HaveCount(32);
    }

    [Test]
    public void ComputeTargetBytes_higher_difficulty_gives_lower_target()
    {
        byte[] targetLow = EtchashMiningHelper.ComputeTargetBytes(1_000_000);
        byte[] targetHigh = EtchashMiningHelper.ComputeTargetBytes(10_000_000);

        var valueLow = new BigInteger(targetLow, isUnsigned: true, isBigEndian: true);
        var valueHigh = new BigInteger(targetHigh, isUnsigned: true, isBigEndian: true);

        valueHigh.Should().BeLessThan(valueLow);
    }

    [Test]
    public void ComputeTargetBytes_difficulty_1_gives_max_target()
    {
        byte[] target = EtchashMiningHelper.ComputeTargetBytes(1);
        var value = new BigInteger(target, isUnsigned: true, isBigEndian: true);
        value.Should().BeGreaterThan(BigInteger.Zero);
    }

    [Test]
    public void ComputeTargetBytes_known_difficulty()
    {
        // difficulty = 2^128 => target = 2^128
        BigInteger diff = BigInteger.Pow(2, 128);
        byte[] target = EtchashMiningHelper.ComputeTargetBytes(diff);

        var targetValue = new BigInteger(target, isUnsigned: true, isBigEndian: true);
        targetValue.Should().Be(BigInteger.Pow(2, 128));
    }
}
