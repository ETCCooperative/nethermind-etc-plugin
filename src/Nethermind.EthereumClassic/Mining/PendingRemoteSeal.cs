// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nethermind.EthereumClassic.Mining;

internal sealed class PendingRemoteSeal<T> : IDisposable where T : class
{
    private readonly T _requestedBlock;
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _cancellationRegistration;

    public PendingRemoteSeal(T requestedBlock, CancellationToken cancellationToken)
    {
        _requestedBlock = requestedBlock;
        _cancellationRegistration = cancellationToken.Register(static state =>
        {
            (TaskCompletionSource<T> completion, CancellationToken token) = ((TaskCompletionSource<T>, CancellationToken))state!;
            completion.TrySetCanceled(token);
        }, (_completion, cancellationToken));

        _completion.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            _cancellationRegistration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public Task<T> Task => _completion.Task;

    public bool TryComplete(T minedBlock)
    {
        if (!ReferenceEquals(_requestedBlock, minedBlock))
            return false;

        return _completion.TrySetResult(minedBlock);
    }

    public bool Cancel() => _completion.TrySetCanceled();

    public void Dispose() => _cancellationRegistration.Dispose();
}
