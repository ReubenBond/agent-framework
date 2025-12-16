// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Telemetry;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Agents.AI.Runtime.Orleans.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace Microsoft.Agents.AI.Runtime.Orleans.Workflows;

/// <summary>
/// Orleans grain implementation for managing a single workflow run.
/// Handles state persistence, SSE streaming, and Orleans Reminders for reliability.
/// Implements retry logic to recover from crashes and drive workflows to completion.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class WorkflowGrain(
    [PersistentState("state")] IPersistentState<WorkflowGrainState> workflowState,
    IGrainFactory grainFactory,
    IWorkflowExecutor workflowExecutor,
    IOptions<RuntimeOptions> runtimeOptions,
    ILogger<WorkflowGrain> logger) : Grain, IWorkflowGrain, IRemindable, IDisposable
{
    private const string ExecutionReminderName = "WorkflowExecution";
    private const int MaxRetryCount = 5;
    private static readonly TimeSpan s_baseRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_executionTimeout = TimeSpan.FromMinutes(10);

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _stateUpdatedEvent = new();
    private readonly StateManager _stateManager = new(workflowState);
    private Task? _backgroundTask;

    private string RunId => this.GetPrimaryKeyString();

    /// <inheritdoc/>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // If we have a workflow, check for pending index updates
        if (workflowState.State.Run is { } run)
        {
            // Sync any pending index updates first
            if (workflowState.State.PendingIndexUpdate)
            {
                await this.TrySyncIndexAsync(cancellationToken).ConfigureAwait(true);
            }

            // If not completed, ensure reminder is registered and start background check
            if (!IsTerminalStatus(run.Status))
            {
                await this.RegisterOrUpdateReminder(
                    ExecutionReminderName,
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1)).ConfigureAwait(true);

                // Start background task to check and retry execution if needed
                // Don't await - let it run in the background
                this.StartBackgroundTaskIfNeeded();
            }
        }
    }

    /// <inheritdoc/>
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await this._shutdownCts.CancelAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.Cancel();

        // Wait for background task to complete gracefully
        if (this._backgroundTask is { IsCompleted: false } task)
        {
            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background task did not complete gracefully for workflow '{RunId}'", this.RunId);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("start", "WorkflowGrain", this.RunId);
        activity?.SetTag(TelemetryConstants.WorkflowName, request.WorkflowName);

        if (workflowState.State.Run is not null)
        {
            throw new InvalidOperationException($"Workflow run '{this.RunId}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = new WorkflowRun
        {
            Id = this.RunId,
            WorkflowName = request.WorkflowName,
            Status = WorkflowRunStatus.Queued,
            Input = request.Input,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = [],
            Artifacts = [],
            PendingRequests = [],
            Metadata = request.Metadata,
            ETag = null
        };

        workflowState.State.Version = 1;
        workflowState.State.Events.Clear();
        workflowState.State.ExecutionState = WorkflowExecutionState.NotStarted;
        workflowState.State.RetryCount = 0;
        workflowState.State.LastExecutionAttempt = null;

        // Add started event
        var startedEvent = new WorkflowStartedEvent
        {
            RunId = this.RunId,
            SequenceNumber = 1,
            Timestamp = now,
            WorkflowName = request.WorkflowName,
            Input = request.Input
        };
        workflowState.State.Events.Add(startedEvent);

        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        // Register with the workflow index
        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.RegisterAsync(new WorkflowRunSummary
        {
            Id = this.RunId,
            WorkflowName = request.WorkflowName,
            Status = WorkflowRunStatus.Queued,
            CreatedAt = now,
            PendingRequestCount = 0
        }, cancellationToken).ConfigureAwait(true);

        // Register reminder for background execution and recovery
        await this.RegisterOrUpdateReminder(
            ExecutionReminderName,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1)).ConfigureAwait(true);

        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' started with workflow name '{WorkflowName}'", this.RunId, request.WorkflowName);

        return this.GetRunWithETag();
    }

    /// <inheritdoc/>
    public Task<WorkflowRun?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.Run is not null ? this.GetRunWithETag() : null);
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> SendSignalAsync(WorkflowSignal signal, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartSignalDelivery(this.RunId, signal.RequestId);

        this.EnsureRunExists();

        var pendingRequest = workflowState.State.Run!.PendingRequests.FirstOrDefault(r => r.RequestId == signal.RequestId);
        if (pendingRequest is null)
        {
            throw new InvalidOperationException($"No pending request with ID '{signal.RequestId}' found for workflow '{this.RunId}'.");
        }

        // Remove the pending request
        workflowState.State.Run = workflowState.State.Run with
        {
            PendingRequests = workflowState.State.Run.PendingRequests.Where(r => r.RequestId != signal.RequestId).ToList(),
            Status = WorkflowRunStatus.Running,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.PendingSignal = signal;
        workflowState.State.ExecutionState = WorkflowExecutionState.ResumeDispatched;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        // Add signal received event
        var signalEvent = new WorkflowSignalReceivedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = DateTimeOffset.UtcNow,
            RequestId = signal.RequestId,
            Response = signal.Response
        };
        workflowState.State.Events.Add(signalEvent);

        workflowState.State.IncrementVersion(null, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' received signal for request '{RequestId}'", this.RunId, signal.RequestId);

        return this.GetRunWithETag();
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> CancelAsync(CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("cancel", "WorkflowGrain", this.RunId);

        this.EnsureRunExists();

        if (IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException($"Cannot cancel workflow '{this.RunId}' - it is already in terminal status '{workflowState.State.Run.Status}'.");
        }

        workflowState.State.CancellationRequested = true;
        workflowState.State.Run = workflowState.State.Run with
        {
            Status = WorkflowRunStatus.Cancelling,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.IncrementVersion(null, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        // Update the workflow index with the new status (with eventual consistency)
        await this.UpdateIndexAsync(cancellationToken).ConfigureAwait(true);

        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' cancellation requested", this.RunId);

        return this.GetRunWithETag();
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> AbortAsync(string reason, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("abort", "WorkflowGrain", this.RunId);
        activity?.SetTag("abort.reason", reason);

        this.EnsureRunExists();

        if (IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException($"Cannot abort workflow '{this.RunId}' - it is already in terminal status '{workflowState.State.Run.Status}'.");
        }

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = workflowState.State.Run with
        {
            Status = WorkflowRunStatus.Aborted,
            UpdatedAt = now,
            CompletedAt = now
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.Failed;
        workflowState.State.PendingSignal = null;

        var abortedEvent = new WorkflowAbortedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = now,
            Reason = reason
        };
        workflowState.State.Events.Add(abortedEvent);

        workflowState.State.IncrementVersion(null, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        // Update the workflow index with the new status (with eventual consistency)
        await this.UpdateIndexAsync(cancellationToken).ConfigureAwait(true);

        await this.UnregisterReminderIfExistsAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogWarning("Workflow '{RunId}' aborted: {Reason}", this.RunId, reason);

        return this.GetRunWithETag();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkflowStatusEvent> StreamEventsAsync(
        int? startingAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (workflowState.State.Run is null)
        {
            yield break;
        }

        var streamedCount = startingAfter ?? 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (streamedCount < workflowState.State.Events.Count)
            {
                yield return workflowState.State.Events[streamedCount];
                streamedCount++;
            }

            if (workflowState.State.Run is { } run && IsTerminalStatus(run.Status))
            {
                break;
            }

            await this._stateUpdatedEvent.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <inheritdoc/>
    public async Task<string> UpdateStatusAsync(WorkflowRunStatusUpdate update, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = workflowState.State.Run! with
        {
            Status = update.Status,
            Error = update.Error,
            UpdatedAt = now,
            CompletedAt = IsTerminalStatus(update.Status) ? now : workflowState.State.Run.CompletedAt
        };

        workflowState.State.ExecutionState = update.Status switch
        {
            WorkflowRunStatus.Running => WorkflowExecutionState.Executing,
            WorkflowRunStatus.WaitingForSignal => WorkflowExecutionState.WaitingForSignal,
            WorkflowRunStatus.Completed => WorkflowExecutionState.Completed,
            WorkflowRunStatus.Failed => WorkflowExecutionState.Failed,
            WorkflowRunStatus.Cancelled => WorkflowExecutionState.Completed,
            WorkflowRunStatus.Aborted => WorkflowExecutionState.Failed,
            _ => workflowState.State.ExecutionState
        };

        if (update.Status is WorkflowRunStatus.Completed or WorkflowRunStatus.Failed
            or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Aborted
            or WorkflowRunStatus.WaitingForSignal)
        {
            workflowState.State.PendingSignal = null;
        }

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        // Update the workflow index with the new status (with eventual consistency)
        await this.UpdateIndexAsync(cancellationToken).ConfigureAwait(true);

        if (IsTerminalStatus(update.Status))
        {
            await this.UnregisterReminderIfExistsAsync().ConfigureAwait(true);
        }

        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public async Task<string> RecordStepStartedAsync(WorkflowStepStartedRecord stepRecord, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        var stepInfo = new WorkflowStepInfo
        {
            StepId = stepRecord.StepId,
            ExecutorId = stepRecord.ExecutorId,
            ExecutorName = stepRecord.ExecutorName,
            StartedAt = stepRecord.StartedAt
        };

        workflowState.State.Run = workflowState.State.Run! with
        {
            Steps = [.. workflowState.State.Run.Steps, stepInfo],
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.Executing;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        var evt = new WorkflowStepStartedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = stepRecord.StartedAt,
            Step = stepRecord
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public async Task<string> RecordStepCompletedAsync(WorkflowStepCompletedRecord stepRecord, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        var steps = workflowState.State.Run!.Steps.ToList();
        var existingStepIndex = steps.FindIndex(s => s.StepId == stepRecord.StepId);
        if (existingStepIndex >= 0)
        {
            steps[existingStepIndex] = steps[existingStepIndex] with
            {
                CompletedAt = stepRecord.CompletedAt,
                Output = stepRecord.Output,
                DurationMs = stepRecord.DurationMs
            };
        }

        workflowState.State.Run = workflowState.State.Run with
        {
            Steps = steps,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        var evt = new WorkflowStepCompletedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = stepRecord.CompletedAt,
            Step = stepRecord
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public async Task<string> RecordPendingRequestAsync(PendingExternalRequest request, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            PendingRequests = [.. workflowState.State.Run.PendingRequests, request],
            Status = WorkflowRunStatus.WaitingForSignal,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.WaitingForSignal;
        workflowState.State.PendingSignal = null;

        var evt = new WorkflowSignalRequestedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = request.RequestedAt,
            Request = request
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        // Update the workflow index with the new status (with eventual consistency)
        await this.UpdateIndexAsync(cancellationToken).ConfigureAwait(true);

        logger.LogInformation("Workflow '{RunId}' waiting for signal on request '{RequestId}' (port: {PortId})",
            this.RunId, request.RequestId, request.PortId);

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public async Task<string> ClearPendingRequestAsync(string requestId, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            PendingRequests = workflowState.State.Run.PendingRequests.Where(r => r.RequestId != requestId).ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public async Task<string> SaveCheckpointAsync(WorkflowCheckpointData checkpoint, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Checkpoints[checkpoint.CheckpointId] = checkpoint;

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        logger.LogDebug("Workflow '{RunId}' checkpoint saved: {CheckpointId}", this.RunId, checkpoint.CheckpointId);

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public Task<WorkflowCheckpointResult?> GetCheckpointAsync(CancellationToken cancellationToken)
    {
        var mostRecentCheckpoint = workflowState.State.Checkpoints.Values
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (mostRecentCheckpoint is null)
        {
            return Task.FromResult<WorkflowCheckpointResult?>(null);
        }

        return Task.FromResult<WorkflowCheckpointResult?>(new WorkflowCheckpointResult
        {
            Checkpoint = mostRecentCheckpoint,
            ETag = workflowState.State.GetETag()
        });
    }

    /// <inheritdoc/>
    public Task<WorkflowCheckpointData?> GetCheckpointByIdAsync(string checkpointId, CancellationToken cancellationToken)
    {
        workflowState.State.Checkpoints.TryGetValue(checkpointId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListCheckpointIdsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(workflowState.State.Checkpoints.Keys.ToList());
    }

    /// <inheritdoc/>
    public async Task<string> RecordArtifactAsync(WorkflowArtifactRecord artifact, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            Artifacts = [.. workflowState.State.Run.Artifacts, artifact],
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var evt = new WorkflowArtifactCreatedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = artifact.CreatedAt,
            Artifact = artifact
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public Task<string?> GetETagAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.Run is not null ? workflowState.State.GetETag() : null);
    }

    /// <inheritdoc/>
    public Task<string?> GetAssignedWorkerIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.AssignedWorkerId);
    }

    /// <inheritdoc/>
    public async Task SetAssignedWorkerIdAsync(string workerId, CancellationToken cancellationToken)
    {
        workflowState.State.AssignedWorkerId = workerId;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        if (!IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException(
                $"Cannot delete workflow '{this.RunId}' - it is in status '{workflowState.State.Run.Status}'. " +
                "Only workflows in terminal status (Completed, Cancelled, Aborted, Failed) can be deleted.");
        }

        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.RemoveAsync(this.RunId, cancellationToken).ConfigureAwait(true);

        await this.UnregisterReminderIfExistsAsync().ConfigureAwait(true);
        await this._stateManager.ClearStateAsync().ConfigureAwait(true);

        logger.LogInformation("Workflow '{RunId}' deleted", this.RunId);

        this.DeactivateOnIdle();
    }

    /// <inheritdoc/>
    public async Task SetExecutionStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        workflowState.State.ExecutionState = state;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public Task<WorkflowExecutionState> GetExecutionStateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.ExecutionState);
    }

    /// <inheritdoc/>
    public async Task SetPendingSignalAsync(WorkflowSignal? signal, CancellationToken cancellationToken)
    {
        workflowState.State.PendingSignal = signal;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public Task<WorkflowSignal?> GetPendingSignalAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.PendingSignal);
    }

    /// <inheritdoc/>
    public async Task<string> RecordOutputDeltaAsync(WorkflowOutputDelta delta, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        var evt = new WorkflowOutputDeltaEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = DateTimeOffset.UtcNow,
            Delta = delta
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        workflowState.State.IncrementVersion(etag, this.RunId);
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    /// <inheritdoc/>
    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ExecutionReminderName)
        {
            return Task.CompletedTask;
        }

        if (workflowState.State.Run is { } run && IsTerminalStatus(run.Status))
        {
            // Fire and forget - don't block the reminder callback
            _ = this.UnregisterReminderIfExistsAsync();
            return Task.CompletedTask;
        }

        // Start background task to check and retry execution
        // Don't await - return immediately to avoid blocking the reminder
        this.StartBackgroundTaskIfNeeded();
        return Task.CompletedTask;
    }

    private async Task CheckAndRetryExecutionAsync(CancellationToken cancellationToken)
    {
        if (workflowState.State.Run is null)
        {
            return;
        }

        var run = workflowState.State.Run;
        var execState = workflowState.State.ExecutionState;
        var lastAttempt = workflowState.State.LastExecutionAttempt;
        var retryCount = workflowState.State.RetryCount;

        if (retryCount >= MaxRetryCount)
        {
            logger.LogWarning("Workflow '{RunId}' exceeded max retry count ({MaxRetries}), aborting",
                this.RunId, MaxRetryCount);

            await this.AbortAsync($"Exceeded maximum retry count ({MaxRetryCount})", cancellationToken).ConfigureAwait(true);
            return;
        }

        var backoffDelay = TimeSpan.FromTicks(Math.Min(
            s_baseRetryDelay.Ticks * (1L << retryCount),
            s_maxRetryDelay.Ticks));

        var timeSinceLastAttempt = lastAttempt.HasValue
            ? DateTimeOffset.UtcNow - lastAttempt.Value
            : TimeSpan.MaxValue;

        var shouldRetry = execState switch
        {
            WorkflowExecutionState.NotStarted when run.Status == WorkflowRunStatus.Queued
                && timeSinceLastAttempt > s_executionTimeout => true,
            WorkflowExecutionState.Dispatched when timeSinceLastAttempt > s_executionTimeout => true,
            WorkflowExecutionState.Executing when timeSinceLastAttempt > s_executionTimeout => true,
            WorkflowExecutionState.ResumeDispatched when timeSinceLastAttempt > s_executionTimeout => true,
            WorkflowExecutionState.Resuming when timeSinceLastAttempt > s_executionTimeout => true,
            WorkflowExecutionState.WaitingForSignal => false,
            WorkflowExecutionState.Completed or WorkflowExecutionState.Failed => false,
            _ => false
        };

        if (!shouldRetry || timeSinceLastAttempt < backoffDelay)
        {
            return;
        }

        logger.LogInformation(
            "Workflow '{RunId}' retrying execution (attempt {Attempt}/{MaxRetries}, state: {State})",
            this.RunId, retryCount + 1, MaxRetryCount, execState);

        workflowState.State.RetryCount = retryCount + 1;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        try
        {
            if (workflowState.State.PendingSignal is { } signal)
            {
                await this.RetryResumeAsync(signal, cancellationToken).ConfigureAwait(true);
            }
            else if (run.Status is WorkflowRunStatus.Queued or WorkflowRunStatus.Running)
            {
                await this.RetryExecuteAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow '{RunId}' retry failed", this.RunId);
        }
    }

    private async Task RetryExecuteAsync(CancellationToken cancellationToken)
    {
        var run = workflowState.State.Run!;
        var callbackBaseUrl = this.GetCallbackBaseUrl();

        var executionRequest = new WorkflowExecutionRequest
        {
            RunId = this.RunId,
            WorkflowName = run.WorkflowName,
            Input = run.Input!,
            CallbackBaseUrl = callbackBaseUrl,
            Options = run.Metadata
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.Dispatched;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        var result = await workflowExecutor.ExecuteAsync(
            executionRequest,
            workflowState.State.AssignedWorkerId,
            cancellationToken).ConfigureAwait(true);

        if (!result.Success)
        {
            logger.LogError(
                "Workflow '{RunId}' retry dispatch failed: {ErrorCode} - {ErrorMessage}",
                this.RunId, result.ErrorCode, result.ErrorMessage);
        }
        else if (!string.IsNullOrEmpty(result.WorkerId))
        {
            workflowState.State.AssignedWorkerId = result.WorkerId;
            await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        }
    }

    private async Task RetryResumeAsync(WorkflowSignal signal, CancellationToken cancellationToken)
    {
        var run = workflowState.State.Run!;
        var callbackBaseUrl = this.GetCallbackBaseUrl();

        var mostRecentCheckpoint = workflowState.State.Checkpoints.Values
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        var resumeRequest = new WorkflowResumeRequest
        {
            RunId = this.RunId,
            WorkflowName = run.WorkflowName,
            CallbackBaseUrl = callbackBaseUrl,
            Signal = signal,
            CheckpointId = mostRecentCheckpoint?.CheckpointId
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.ResumeDispatched;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        var result = await workflowExecutor.ResumeAsync(
            resumeRequest,
            workflowState.State.AssignedWorkerId,
            cancellationToken).ConfigureAwait(true);

        if (!result.Success)
        {
            logger.LogError(
                "Workflow '{RunId}' retry resume dispatch failed: {ErrorCode} - {ErrorMessage}",
                this.RunId, result.ErrorCode, result.ErrorMessage);
        }
        else if (!string.IsNullOrEmpty(result.WorkerId))
        {
            workflowState.State.AssignedWorkerId = result.WorkerId;
            await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        }
    }

    private string GetCallbackBaseUrl()
    {
        return runtimeOptions.Value.CallbackBaseUrl
            ?? Environment.GetEnvironmentVariable("RUNTIME_CALLBACK_URL")
            ?? "http://localhost:5000";
    }

    private void EnsureRunExists()
    {
        if (workflowState.State.Run is null)
        {
            throw WorkflowNotFoundException.ForRunId(this.RunId);
        }
    }

    private WorkflowRun GetRunWithETag()
    {
        Debug.Assert(workflowState.State.Run is not null);
        return workflowState.State.Run with { ETag = workflowState.State.GetETag() };
    }

    private static bool IsTerminalStatus(WorkflowRunStatus status)
    {
        return status is WorkflowRunStatus.Completed
            or WorkflowRunStatus.Cancelled
            or WorkflowRunStatus.Aborted
            or WorkflowRunStatus.Failed;
    }

    private async Task UnregisterReminderIfExistsAsync()
    {
        try
        {
            var reminder = await this.GetReminder(ExecutionReminderName).ConfigureAwait(true);
            if (reminder is not null)
            {
                await this.UnregisterReminder(reminder).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unregister reminder for workflow '{RunId}'", this.RunId);
        }
    }

    /// <summary>
    /// Starts a background task to check and retry execution if one isn't already running.
    /// This pattern avoids blocking grain activation or reminder callbacks.
    /// </summary>
    private void StartBackgroundTaskIfNeeded()
    {
        if (this._backgroundTask is null or { IsCompleted: true })
        {
            this._backgroundTask = this.RunBackgroundExecutionCheckAsync();
        }
    }

    /// <summary>
    /// Background task that yields immediately and then performs execution check.
    /// Uses the pattern from https://gist.github.com/ReubenBond/8dc47f68634faafe334fadc6f5444ad0
    /// </summary>
    private async Task RunBackgroundExecutionCheckAsync()
    {
        // Yield immediately to avoid blocking the caller (OnActivateAsync or ReceiveReminder)
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (this._shutdownCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // First, sync any pending index updates
            await this.TrySyncIndexAsync(this._shutdownCts.Token).ConfigureAwait(true);

            // Then check and retry execution
            await this.CheckAndRetryExecutionAsync(this._shutdownCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (this._shutdownCts.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background execution check failed for workflow '{RunId}'", this.RunId);
        }
    }

    /// <summary>
    /// Attempts to sync the workflow status to the index if there's a pending update.
    /// Clears the pending flag on success.
    /// </summary>
    private async Task TrySyncIndexAsync(CancellationToken cancellationToken)
    {
        if (!workflowState.State.PendingIndexUpdate || workflowState.State.Run is null)
        {
            return;
        }

        try
        {
            var run = workflowState.State.Run;
            var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
            await indexGrain.UpdateAsync(
                this.RunId,
                run.Status,
                run.PendingRequests.Count,
                cancellationToken).ConfigureAwait(true);

            workflowState.State.PendingIndexUpdate = false;
            await this._stateManager.WriteStateAsync().ConfigureAwait(true);

            logger.LogDebug("Synced pending index update for workflow '{RunId}'", this.RunId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync index for workflow '{RunId}', will retry later", this.RunId);
        }
    }

    /// <summary>
    /// Updates the workflow index with the current status.
    /// Sets PendingIndexUpdate flag before the update and clears it after success.
    /// If the update fails, the flag remains set for retry on next activation/reminder.
    /// </summary>
    private async Task UpdateIndexAsync(CancellationToken cancellationToken)
    {
        if (workflowState.State.Run is null)
        {
            return;
        }

        // Set the pending flag before attempting the update
        workflowState.State.PendingIndexUpdate = true;
        await this._stateManager.WriteStateAsync().ConfigureAwait(true);

        try
        {
            var run = workflowState.State.Run;
            var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
            await indexGrain.UpdateAsync(
                this.RunId,
                run.Status,
                run.PendingRequests.Count,
                cancellationToken).ConfigureAwait(true);

            // Clear the pending flag on success
            workflowState.State.PendingIndexUpdate = false;
            await this._stateManager.WriteStateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Log but don't throw - the pending flag remains set for retry
            logger.LogWarning(ex, "Failed to update index for workflow '{RunId}', will retry later", this.RunId);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this._shutdownCts.Dispose();
    }
}
