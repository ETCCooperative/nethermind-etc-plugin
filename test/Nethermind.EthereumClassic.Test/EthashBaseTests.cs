// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

/// <summary>
/// Unit tests for EthashBase static methods.
/// These test the core Ethash algorithm implementation.
/// </summary>
[TestFixture]
public class EthashBaseTests
{
    [Test]
    public void GetEpoch_BlockZero_ReturnsEpochZero()
    {
        EthashBase.GetEpoch(0).Should().Be(0);
    }

    [Test]
    public void GetEpoch_LastBlockOfEpochZero_ReturnsEpochZero()
    {
        EthashBase.GetEpoch(29999).Should().Be(0);
    }

    [Test]
    public void GetEpoch_FirstBlockOfEpochOne_ReturnsEpochOne()
    {
        EthashBase.GetEpoch(30000).Should().Be(1);
    }

    [Test]
    public void GetEpoch_BlockInEpochTwo_ReturnsEpochTwo()
    {
        EthashBase.GetEpoch(60000).Should().Be(2);
    }

    [Test]
    public void GetCacheSize_EpochZero_ReturnsValidSize()
    {
        var cacheSize = EthashBase.GetCacheSize(0);
        cacheSize.Should().BeGreaterThan(0);
        // Cache size should be a multiple of hash bytes
        (cacheSize % EthashBase.HashBytes).Should().Be(0);
    }

    [Test]
    public void GetDataSize_EpochZero_ReturnsValidSize()
    {
        var dataSize = EthashBase.GetDataSize(0);
        dataSize.Should().BeGreaterThan(0);
        // Data size should be a multiple of mix bytes
        (dataSize % EthashBase.MixBytes).Should().Be(0);
    }

    [Test]
    [TestCase(10u, 7u)]
    [TestCase(100u, 97u)]
    [TestCase(2u, 2u)]
    [TestCase(3u, 3u)]
    [TestCase(5u, 5u)]
    public void FindLargestPrime_ReturnsExpectedPrime(uint upper, uint expected)
    {
        EthashBase.FindLargestPrime(upper).Should().Be(expected);
    }

    [Test]
    [TestCase(2u, true)]
    [TestCase(3u, true)]
    [TestCase(5u, true)]
    [TestCase(7u, true)]
    [TestCase(11u, true)]
    [TestCase(97u, true)]
    [TestCase(0u, false)]
    [TestCase(1u, false)]
    [TestCase(4u, false)]
    [TestCase(6u, false)]
    [TestCase(100u, false)]
    public void IsPrime_IdentifiesPrimesCorrectly(uint number, bool expected)
    {
        EthashBase.IsPrime(number).Should().Be(expected);
    }

    [Test]
    public void CacheSizes_GrowWithEpoch()
    {
        var cache0 = EthashBase.GetCacheSize(0);
        var cache100 = EthashBase.GetCacheSize(100);
        var cache200 = EthashBase.GetCacheSize(200);

        cache100.Should().BeGreaterThan(cache0);
        cache200.Should().BeGreaterThan(cache100);
    }

    [Test]
    public void DataSizes_GrowWithEpoch()
    {
        var data0 = EthashBase.GetDataSize(0);
        var data100 = EthashBase.GetDataSize(100);
        var data200 = EthashBase.GetDataSize(200);

        data100.Should().BeGreaterThan(data0);
        data200.Should().BeGreaterThan(data100);
    }

    [Test]
    public void EpochLength_IsCorrect()
    {
        EthashBase.EpochLength.Should().Be(30000);
    }

    [Test]
    public void HashBytes_IsCorrect()
    {
        EthashBase.HashBytes.Should().Be(64);
    }

    [Test]
    public void MixBytes_IsCorrect()
    {
        EthashBase.MixBytes.Should().Be(128);
    }

    [Test]
    public void CacheRounds_IsCorrect()
    {
        EthashBase.CacheRounds.Should().Be(3);
    }

    [Test]
    public void Accesses_IsCorrect()
    {
        EthashBase.Accesses.Should().Be(64);
    }
}
