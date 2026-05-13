// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

[TestFixture]
public class EtchashHintBasedCacheTests
{
    [Test]
    public void Hint_AddsAndRemovesEpochRefsPerGuid()
    {
        EtchashHintBasedCache cache = new(_ => new TestDataSet());
        Guid firstGuid = Guid.NewGuid();
        Guid secondGuid = Guid.NewGuid();
        EtchashCacheEpoch epoch = new(DagEpoch: 195, SeedEpoch: 390);

        cache.Hint(firstGuid, [epoch]);
        cache.Hint(secondGuid, [epoch]);

        cache.CachedEpochsCount.Should().Be(1);
        cache.Get(epoch).Should().NotBeNull();

        cache.Hint(firstGuid, []);
        cache.CachedEpochsCount.Should().Be(1);
        cache.Get(epoch).Should().NotBeNull();

        cache.Hint(secondGuid, []);
        cache.CachedEpochsCount.Should().Be(0);
        cache.Get(epoch).Should().BeNull();
    }

    [Test]
    public void Hint_ReusesRecentlyReleasedEpoch()
    {
        int builds = 0;
        EtchashHintBasedCache cache = new(_ =>
        {
            builds++;
            return new TestDataSet();
        });

        Guid guid = Guid.NewGuid();
        EtchashCacheEpoch epoch = new(DagEpoch: 195, SeedEpoch: 390);

        cache.Hint(guid, [epoch]);
        IEthashDataSet? firstDataSet = cache.Get(epoch);
        cache.Hint(guid, []);
        cache.Hint(guid, [epoch]);

        cache.Get(epoch).Should().BeSameAs(firstDataSet);
        builds.Should().Be(1);
    }

    [Test]
    public void Hint_RejectsOverlyWideRanges()
    {
        EtchashHintBasedCache cache = new(_ => new TestDataSet());
        EtchashCacheEpoch[] epochs = Enumerable.Range(0, 12)
            .Select(i => new EtchashCacheEpoch((uint)i, (uint)i))
            .ToArray();

        Action act = () => cache.Hint(Guid.NewGuid(), epochs);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Hint too wide");
    }

    [Test]
    public void Dispose_Disposes_Cached_And_Recently_Released_Datasets()
    {
        List<TestDataSet> created = [];
        EtchashHintBasedCache cache = new(_ =>
        {
            TestDataSet dataSet = new();
            created.Add(dataSet);
            return dataSet;
        });

        Guid guid = Guid.NewGuid();
        EtchashCacheEpoch active = new(DagEpoch: 195, SeedEpoch: 390);
        EtchashCacheEpoch released = new(DagEpoch: 196, SeedEpoch: 392);

        cache.Hint(guid, [released]);
        cache.Get(released);
        cache.Hint(guid, [active]); // released moves into the recent pool
        cache.Get(active);

        created.Should().HaveCount(2);
        cache.Dispose();

        created.Should().AllSatisfy(ds => ds.DisposeCount.Should().Be(1));
    }

    [Test]
    public void Hint_Across_Ecip1099_Transition_Builds_Both_Sides()
    {
        const long ecip1099Transition = 11_700_000;
        EtchashEpochCalculator calculator = new(ecip1099Transition);
        EtchashHintBasedCache cache = new(epoch => new TestDataSet(epoch.SeedEpoch));
        IReadOnlyList<EtchashCacheEpoch> epochs =
            calculator.GetCacheEpochs(ecip1099Transition - 1, ecip1099Transition);

        cache.Hint(Guid.NewGuid(), epochs);

        cache.CachedEpochsCount.Should().Be(2);
        ((TestDataSet)cache.Get(epochs[0])!).SeedEpoch.Should().Be(389);
        ((TestDataSet)cache.Get(epochs[1])!).SeedEpoch.Should().Be(390);
    }

    private sealed class TestDataSet : IEthashDataSet
    {
        public TestDataSet(uint seedEpoch = 0) => SeedEpoch = seedEpoch;

        public uint SeedEpoch { get; }

        public int DisposeCount { get; private set; }

        public uint Size => 0;

        public uint[] CalcDataSetItem(uint i) => [];

        public void Dispose() => DisposeCount++;
    }
}
