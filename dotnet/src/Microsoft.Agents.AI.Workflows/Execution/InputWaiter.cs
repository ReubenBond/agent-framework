// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Workflows.Execution;

/// <summary>
/// A synchronization primitive that allows a producer to signal that input is available,
/// and a consumer to wait for that signal. This is used to coordinate between the run loop
/// and external code that enqueues messages or responses.
/// </summary>
/// <remarks>
/// <para>
/// This class implements a binary semaphore pattern where multiple signals before a wait
/// are coalesced into a single wake-up. This is intentional - the consumer just needs to
/// know "there's work to do", not "how many times were we signaled".
/// </para>
/// <para>
/// The wait methods support optional timeouts for polling scenarios where the consumer
/// wants to periodically check for cancellation or other conditions.
/// </para>
/// </remarks>
internal sealed class InputWaiter : IDisposable
{
    private readonly SemaphoreSlim _inputSignal = new(initialCount: 0, maxCount: 1);
    private int _isDisposed;

    /// <summary>
    /// Releases resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref this._isDisposed, 1, 0) == 0)
        {
            this._inputSignal.Dispose();
        }
    }

    /// <summary>
    /// Signals that new input has been provided and the waiter should continue processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is safe to call multiple times. If the waiter is already signaled
    /// (semaphore count is 1), additional signals are ignored (coalesced).
    /// </para>
    /// <para>
    /// This method is safe to call after disposal - it will simply return without effect.
    /// </para>
    /// </remarks>
    public void SignalInput()
    {
        // Check if disposed to avoid ObjectDisposedException
        if (Volatile.Read(ref this._isDisposed) != 0)
        {
            return;
        }

        // Release the run loop to process more work
        // Only release if not already signaled (binary semaphore behavior)
        try
        {
            this._inputSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled - this is expected and safe to ignore.
            // Multiple signals before a wait are coalesced into one wake-up.
        }
        catch (ObjectDisposedException)
        {
            // Disposed between our check and the Release call - safe to ignore
        }
    }

    /// <summary>
    /// Waits indefinitely for a signal to be raised.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task that completes when a signal is received.</returns>
    /// <exception cref="OperationCanceledException">The cancellation token was triggered.</exception>
    public Task WaitForInputAsync(CancellationToken cancellationToken = default)
    {
        return this._inputSignal.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for a signal to be raised, with an optional timeout.
    /// </summary>
    /// <param name="timeout">
    /// The maximum time to wait, or <c>null</c> to wait indefinitely.
    /// Use <see cref="TimeSpan.Zero"/> to check without blocking.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>
    /// <c>true</c> if a signal was received; <c>false</c> if the timeout elapsed.
    /// </returns>
    /// <exception cref="OperationCanceledException">The cancellation token was triggered.</exception>
    public Task<bool> WaitForInputAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        return this._inputSignal.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
