// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading;

namespace Nethermind.EthereumClassic.Mining;

/// <summary>
/// Tracks the single in-flight remote sealing request. Replacing the active request
/// cancels the previous pending task so that <c>SealBlock</c> callers do not hang
/// forever when a new block supersedes their request.
/// </summary>
internal sealed class PendingRemoteSealRegistry<T> where T : class
{
    private readonly Lock _lock = new();
    private PendingRemoteSeal<T>? _active;

    public PendingRemoteSeal<T> Replace(T requestedBlock, CancellationToken cancellationToken)
    {
        PendingRemoteSeal<T> next = new(requestedBlock, cancellationToken);
        lock (_lock)
        {
            _active?.Cancel();
            _active = next;
        }

        return next;
    }

    public bool TryCompleteActive(T minedBlock)
    {
        lock (_lock)
        {
            if (_active is null || !_active.TryComplete(minedBlock))
                return false;

            _active = null;
            return true;
        }
    }
}
