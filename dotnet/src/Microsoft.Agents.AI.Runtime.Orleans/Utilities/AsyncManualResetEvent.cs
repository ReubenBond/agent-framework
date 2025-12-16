// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Runtime.Orleans.Utilities;

/// <summary>
/// An async manual reset event that can be used to coordinate async operations.
/// </summary>
public sealed class AsyncManualResetEvent
{
    private readonly object _lock = new();
    private TaskCompletionSource _event = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Waits for the event to be signaled.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (this._lock)
        {
            completion = this._event;
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals the event and resets it for the next wait.
    /// </summary>
    public void SignalAndReset()
    {
        TaskCompletionSource completion;

        lock (this._lock)
        {
            completion = this._event;
            this._event = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        completion.TrySetResult();
    }

    /// <summary>
    /// Cancels all waiting operations.
    /// </summary>
    public void Cancel()
    {
        TaskCompletionSource completion;

        lock (this._lock)
        {
            completion = this._event;
        }

        completion.TrySetCanceled();
    }
}
