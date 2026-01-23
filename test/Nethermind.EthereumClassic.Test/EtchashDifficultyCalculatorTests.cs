// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;
using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.EthereumClassic.Test;

/// <summary>
/// Unit tests for Ethereum Classic difficulty bomb behavior.
/// </summary>
[TestFixture]
public class DifficultyBombCalculatorTests
{
    // ETC Mainnet fork blocks
    private const long DieHard = 3_000_000;
    private const long Gotham = 5_000_000;
    private const long Ecip1041 = 5_900_000;

    [Test]
    public void TimeBomb_PreActivation_Is_Zero()
    {
        // Bomb doesn't activate until block 200,000
        var bomb = DifficultyBombCalculator.CalculateTimeBomb(100_000, DieHard, Gotham, Ecip1041);
        bomb.Should().Be(BigInteger.Zero);
    }

    [Test]
    public void TimeBomb_Grows_Exponentially()
    {
        // Block 300k: period=3, bomb=2^1; Block 400k: period=4, bomb=2^2
        var bomb300k = DifficultyBombCalculator.CalculateTimeBomb(300_000, DieHard, Gotham, Ecip1041);
        var bomb400k = DifficultyBombCalculator.CalculateTimeBomb(400_000, DieHard, Gotham, Ecip1041);

        bomb300k.Should().Be(BigInteger.Pow(2, 1));
        bomb400k.Should().Be(BigInteger.Pow(2, 2));
    }

    [Test]
    public void DieHard_Pauses_Bomb()
    {
        // DieHard pauses at period 30, bomb = 2^28
        var bombAt3M = DifficultyBombCalculator.CalculateTimeBomb(3_000_000, DieHard, Gotham, Ecip1041);
        var bombAt4M = DifficultyBombCalculator.CalculateTimeBomb(4_000_000, DieHard, Gotham, Ecip1041);

        var expected = BigInteger.Pow(2, 28);
        bombAt3M.Should().Be(expected);
        bombAt4M.Should().Be(expected, "Bomb should stay paused");
    }

    [Test]
    public void Gotham_Resumes_Bomb_With_Delay()
    {
        var bombAtGotham = DifficultyBombCalculator.CalculateTimeBomb(5_000_000, DieHard, Gotham, Ecip1041);
        bombAtGotham.Should().BeGreaterThan(BigInteger.Zero);
    }

    [Test]
    public void Ecip1041_Removes_Bomb()
    {
        var bombBefore = DifficultyBombCalculator.CalculateTimeBomb(5_899_999, DieHard, Gotham, Ecip1041);
        var bombAt = DifficultyBombCalculator.CalculateTimeBomb(5_900_000, DieHard, Gotham, Ecip1041);
        var bombAfter = DifficultyBombCalculator.CalculateTimeBomb(100_000_000, DieHard, Gotham, Ecip1041);

        bombBefore.Should().BeGreaterThan(BigInteger.Zero);
        bombAt.Should().Be(BigInteger.Zero);
        bombAfter.Should().Be(BigInteger.Zero);
    }

    [Test]
    public void Mordor_Has_No_Bomb()
    {
        // Mordor: all transitions null = no bomb ever
        var bomb = DifficultyBombCalculator.CalculateTimeBomb(50_000_000, null, null, null);
        bomb.Should().Be(BigInteger.Zero);
    }
}
