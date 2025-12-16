// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Orleans.Core;

namespace Microsoft.Agents.AI.Runtime.Orleans.Utilities;

/// <summary>
/// A helper class which wraps a grain state instance and ensures that only a single write operation
/// is outstanding at any moment in time. This prevents ETag violations when a reentrant grain
/// has concurrent tasks attempting to write state.
/// </summary>
/// <remarks>
/// When a reentrant grain is doing WriteStateAsync, ETag violations are possible due to concurrent writes.
/// The solution is to serialize and batch writes, ensuring only a single write is outstanding at any moment.
/// Based on pattern from: https://github.com/microsoft/autogen
/// </remarks>
/// <param name="state">The grain state.</param>
internal sealed class StateManager(IStorage state)
{
    /// <summary>
    /// Allows state writing to happen in the background.
    /// </summary>
    private Task? _pendingOperation;

    /// <summary>
    /// Writes the grain state, ensuring only one write is outstanding at a time.
    /// If another write is in progress, waits for it and then performs a new write
    /// (since our changes weren't included in that write).
    /// </summary>
    public async ValueTask WriteStateAsync()
    {
        await this.PerformOperationAsync(static state => state.WriteStateAsync()).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the grain state, ensuring only one operation is outstanding at a time.
    /// </summary>
    public async ValueTask ClearStateAsync()
    {
        await this.PerformOperationAsync(static state => state.ClearStateAsync()).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a state operation, ensuring only one operation is outstanding at a time.
    /// </summary>
    /// <param name="performOperation">The operation to perform on the state.</param>
    public async ValueTask PerformOperationAsync(Func<IStorage, Task> performOperation)
    {
        if (this._pendingOperation is Task currentWriteStateOperation)
        {
            // Await the outstanding write, but ignore any exceptions since it doesn't include our changes.
            // We'll perform our own write after.
            await currentWriteStateOperation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (this._pendingOperation == currentWriteStateOperation)
            {
                // Only null out the outstanding operation if it's the same one we awaited,
                // otherwise another request might have already done so.
                this._pendingOperation = null;
            }
        }

        Task operation;
        if (this._pendingOperation is null)
        {
            // If after the initial write completed, no other request initiated a new write operation, do it now.
            operation = performOperation(state);
            this._pendingOperation = operation;
        }
        else
        {
            // If there were many requests enqueued to persist state, there is no reason to enqueue a new write
            // operation for each, since any write (after the initial one that we already awaited) will have
            // cumulative changes including the one requested by our caller. Just await the new outstanding write.
            operation = this._pendingOperation;
        }

        try
        {
            await operation.ConfigureAwait(false);
        }
        finally
        {
            if (this._pendingOperation == operation)
            {
                // Only null out the outstanding operation if it's the same one we awaited,
                // otherwise another request might have already done so.
                this._pendingOperation = null;
            }
        }
    }
}
