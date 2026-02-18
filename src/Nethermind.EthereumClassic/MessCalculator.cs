// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0
//
// Port of core-geth/core/blockchain_af.go — ECBP-1100 "MESS" artificial finality.

using Nethermind.Int256;

namespace Nethermind.EthereumClassic;

/// <summary>
/// MESS (Modified Exponential Subjective Scoring) calculator.
/// Implements the polynomial antigravity function from ECBP-1100.
/// </summary>
internal static class MessCalculator
{
    // CURVE_FUNCTION_DENOMINATOR = 128
    private static readonly UInt256 Denominator = 128;

    // xcap = floor(8000 * pi) = 25132  (~7 hours in seconds)
    private static readonly UInt256 XCap = 25132;

    // ampl = 15, height = 128 * (15 * 2) = 3840
    private static readonly UInt256 Height = 3840;

    /// <summary>
    /// Computes the antigravity polynomial value for a given time delta in seconds.
    /// Returns a value from 128 (at timeDelta=0) to 3968 (at timeDelta>=25132).
    /// </summary>
    public static UInt256 PolynomialV(ulong timeDeltaSeconds)
    {
        UInt256 x = timeDeltaSeconds;
        if (x > XCap)
            x = XCap;

        // 3 * x^2
        UInt256 x2 = x * x;
        UInt256 term1 = 3 * x2;

        // 2 * x^3 / xcap
        UInt256 x3 = x2 * x;
        UInt256 term2 = 2 * x3 / XCap;

        // (3*x^2 - 2*x^3/xcap) * height / xcap^2
        UInt256 xcap2 = XCap * XCap;
        UInt256 result = (term1 - term2) * Height / xcap2;

        return Denominator + result;
    }

    /// <summary>
    /// Determines whether a reorg should be rejected by MESS.
    /// </summary>
    /// <param name="commonAncestorTD">Total difficulty at the common ancestor.</param>
    /// <param name="localTD">Total difficulty of the current canonical head.</param>
    /// <param name="proposedTD">Total difficulty of the proposed (competing) head.</param>
    /// <param name="commonAncestorTime">Timestamp of the common ancestor block.</param>
    /// <param name="currentHeadTime">Timestamp of the current canonical head.</param>
    /// <returns>True if the reorg should be rejected.</returns>
    public static bool ShouldRejectReorg(
        UInt256 commonAncestorTD,
        UInt256 localTD,
        UInt256 proposedTD,
        ulong commonAncestorTime,
        ulong currentHeadTime)
    {
        // Subchain TDs from common ancestor
        UInt256 proposedSubchainTD = proposedTD - commonAncestorTD;
        UInt256 localSubchainTD = localTD - commonAncestorTD;

        ulong timeDelta = currentHeadTime >= commonAncestorTime
            ? currentHeadTime - commonAncestorTime
            : 0;

        UInt256 antigravity = PolynomialV(timeDelta);

        // Reject if: proposedSubchainTD * 128 < antigravity * localSubchainTD
        return proposedSubchainTD * Denominator < antigravity * localSubchainTD;
    }
}
