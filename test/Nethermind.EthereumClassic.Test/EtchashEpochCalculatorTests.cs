// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

[TestFixture]
public class EtchashEpochCalculatorTests
{
    private const long Ecip1099Transition = 11_700_000;

    private readonly EtchashEpochCalculator _calculator = new(Ecip1099Transition);

    [Test]
    public void GetCacheEpoch_BeforeTransition_UsesEthashEpochAsSeed()
    {
        _calculator.GetCacheEpoch(Ecip1099Transition - 1)
            .Should().Be(new EtchashCacheEpoch(DagEpoch: 389, SeedEpoch: 389));
    }

    [Test]
    public void GetCacheEpoch_AtTransition_UsesHalvedDagEpochAndDoubledSeed()
    {
        _calculator.GetCacheEpoch(Ecip1099Transition)
            .Should().Be(new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390));
    }

    [Test]
    public void GetCacheEpochs_CrossingTransition_ReturnsBothSides()
    {
        _calculator.GetCacheEpochs(Ecip1099Transition - 1, Ecip1099Transition)
            .Should().Equal(
                new EtchashCacheEpoch(DagEpoch: 389, SeedEpoch: 389),
                new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390));
    }

    [Test]
    public void GetCacheEpochs_AfterTransition_SkipsUnusedOddSeedEpochs()
    {
        _calculator.GetCacheEpochs(Ecip1099Transition, Ecip1099Transition + 120_000)
            .Should().Equal(
                new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390),
                new EtchashCacheEpoch(DagEpoch: 196, SeedEpoch: 392),
                new EtchashCacheEpoch(DagEpoch: 197, SeedEpoch: 394));
    }

    [Test]
    public void GetCacheEpochs_RejectsOverlyWideRangesBeforeAllocatingAllEpochs()
    {
        Action act = () => _calculator.GetCacheEpochs(0, 330_000, maxEpochs: 11);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Hint too wide");
    }
}
