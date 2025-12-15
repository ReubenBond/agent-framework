// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// Service for hosting and executing workflows.
/// Implements IWorkflowHost to handle workflow execution requests from the Gateway.
/// Workflows are discovered from the DI container as keyed services of type <see cref="Workflow"/>.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class WorkflowHostService : IWorkflowHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowHostService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowHostService"/> class.
    /// </summary>
    public WorkflowHostService(
        IServiceProvider serviceProvider,
        ILogger<WorkflowHostService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this._serviceProvider = serviceProvider;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<WorkflowStatusEvent> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.ExecuteInternalAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<WorkflowStatusEvent> ExecuteInternalAsync(
        WorkflowExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartWorkflowExecution(request.RunId, request.WorkflowName, ActivityKind.Server);

        this._logger.LogInformation("Starting workflow execution: {RunId} (workflow: {WorkflowName})",
            request.RunId, request.WorkflowName);

        // Create state client for callbacks
        var stateClient = CreateStateClient(request.CallbackBaseUrl);

        // Resolve a framework Workflow instance
        var workflow = this._serviceProvider.GetKeyedService<Workflow>(request.WorkflowName);
        if (workflow is null)
        {
            this._logger.LogError("Unknown workflow: {WorkflowName}", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"Unknown workflow: '{request.WorkflowName}'"));
            yield return new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 1,
                Timestamp = DateTimeOffset.UtcNow,
                Error = new WorkflowErrorInfo
                {
                    Code = "WORKFLOW_NOT_FOUND",
                    Message = $"Unknown workflow: '{request.WorkflowName}'"
                }
            };
            yield break;
        }

        this._logger.LogDebug("Executing workflow: {WorkflowName}", request.WorkflowName);

        // Use a channel to safely collect events from the framework workflow
        var channel = Channel.CreateUnbounded<WorkflowStatusEvent>();

        // Execute framework workflow in background and write to channel
        var executionTask = this.ExecuteWorkflowCoreAsync(
            request,
            workflow,
            stateClient,
            channel.Writer,
            cancellationToken);

        // Read events from channel and yield them
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Ensure execution task completes
        await executionTask;
    }

    /// <summary>
    /// Executes a <see cref="Workflow"/> using <see cref="InProcessExecution"/>.
    /// Maps framework events to contract events for SSE streaming.
    /// </summary>
    private async Task ExecuteWorkflowCoreAsync(
        WorkflowExecutionRequest request,
        Workflow workflow,
        GatewayWorkflowStateClient stateClient,
        ChannelWriter<WorkflowStatusEvent> writer,
        CancellationToken cancellationToken)
    {
        Checkpointed<StreamingRun>? checkpointedRun = null;
        int sequenceNumber = 0;
        Dictionary<string, DateTimeOffset> stepStartTimes = [];
        Dictionary<string, string> executorStepIds = []; // Map executorId to stepId for completion

        try
        {
            // Update status to Running
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running },
                etag: null,
                cancellationToken);

            // Emit workflow started event
            await writer.WriteAsync(new AgentContracts.Workflows.WorkflowStartedEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber++,
                Timestamp = DateTimeOffset.UtcNow,
                WorkflowName = request.WorkflowName,
                Input = request.Input
            }, cancellationToken);

            // Get the accepted input types from the workflow's starting executor
            var protocolDescriptor = await workflow.DescribeProtocolAsync(cancellationToken);
            var acceptedInputTypes = protocolDescriptor.Accepts.ToArray();
            this._logger.LogDebug("[DIAG] Workflow {WorkflowName} accepts input types: {AcceptedTypes}",
                request.WorkflowName,
                string.Join(", ", acceptedInputTypes.Select(t => t.FullName)));

            // Deserialize input and start workflow execution with Gateway-backed checkpoint manager
            var inputData = this.DeserializeInput(request.Input, acceptedInputTypes);
            var inputType = inputData.GetType();
            this._logger.LogDebug("[DIAG] Input TypeName from message: {TypeName}, Deserialized type: {DeserializedType}",
                request.Input.TypeName,
                inputType.FullName);
            var checkpointManager = GatewayCheckpointStore.CreateCheckpointManager(stateClient, request.RunId);

            // CRITICAL: Use the non-generic StreamAsync and then send the message with its runtime type.
            // This preserves the actual message type (e.g., MarketingContentInput) for workflow routing.
            // Using the generic StreamAsync<TInput> with inputData would use 'object' as TInput, which
            // would cause the message router to fail to match any handlers.
            var executionEnvironment = InProcessExecution.WithLogger(this._logger);
            checkpointedRun = await executionEnvironment.StreamAsync(workflow, checkpointManager, request.RunId, cancellationToken);
            var messageSent = await checkpointedRun.Run.TrySendMessageUntypedAsync(inputData, inputType);

            if (!messageSent)
            {
                this._logger.LogError(
                    "Workflow {WorkflowName} does not accept input of type {InputType}. The workflow's starting executor may not have a handler for this message type.",
                    request.WorkflowName, inputType.FullName);
                throw new InvalidOperationException(
                    $"Workflow '{request.WorkflowName}' does not accept input of type '{inputType.FullName}'. " +
                    "Ensure the workflow's starting executor has a handler for this message type.");
            }

            // Process framework events and map to contract events
            this._logger.LogDebug("[DIAG] Starting WatchStreamAsync loop for workflow {RunId}", request.RunId);
            await foreach (var evt in checkpointedRun.Run.WatchStreamAsync(cancellationToken))
            {
                this._logger.LogDebug("[DIAG] Received event type: {EventType} for workflow {RunId}", evt.GetType().Name, request.RunId);
                switch (evt)
                {
                    case ExecutorInvokedEvent executorInvoked:
                        this._logger.LogDebug("[DIAG] ExecutorInvokedEvent: ExecutorId={ExecutorId}", executorInvoked.ExecutorId);
                        var stepId = $"step_{sequenceNumber}";
                        var startedAt = DateTimeOffset.UtcNow;
                        stepStartTimes[executorInvoked.ExecutorId] = startedAt;
                        executorStepIds[executorInvoked.ExecutorId] = stepId;

                        var stepStartedRecord = new WorkflowStepStartedRecord
                        {
                            StepId = stepId,
                            ExecutorId = executorInvoked.ExecutorId,
                            ExecutorName = executorInvoked.ExecutorId,
                            StartedAt = startedAt
                        };

                        // Record step to Gateway for persistence
                        await stateClient.RecordStepStartedAsync(
                            request.RunId,
                            stepStartedRecord,
                            etag: null,
                            cancellationToken);

                        // Emit SSE event
                        await writer.WriteAsync(new WorkflowStepStartedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = startedAt,
                            Step = stepStartedRecord
                        }, cancellationToken);
                        break;

                    case ExecutorCompletedEvent executorCompleted:
                        this._logger.LogDebug("[DIAG] ExecutorCompletedEvent: ExecutorId={ExecutorId}, DataType={DataType}",
                            executorCompleted.ExecutorId, executorCompleted.Data?.GetType().Name ?? "null");
                        var completedStepId = executorStepIds.GetValueOrDefault(executorCompleted.ExecutorId, $"step_{sequenceNumber}");
                        var stepStartedAt = stepStartTimes.GetValueOrDefault(executorCompleted.ExecutorId, DateTimeOffset.UtcNow);
                        var completedAt = DateTimeOffset.UtcNow;
                        var durationMs = (long)(completedAt - stepStartedAt).TotalMilliseconds;

                        var stepCompletedRecord = new WorkflowStepCompletedRecord
                        {
                            StepId = completedStepId,
                            ExecutorId = executorCompleted.ExecutorId,
                            CompletedAt = completedAt,
                            Output = executorCompleted.Data is not null
                                ? WorkflowMessage.Create(executorCompleted.Data)
                                : null,
                            DurationMs = durationMs
                        };

                        // Record step completion to Gateway for persistence
                        await stateClient.RecordStepCompletedAsync(
                            request.RunId,
                            stepCompletedRecord,
                            etag: null,
                            cancellationToken);

                        // Emit SSE event
                        await writer.WriteAsync(new WorkflowStepCompletedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = completedAt,
                            Step = stepCompletedRecord
                        }, cancellationToken);
                        break;

                    case RequestInfoEvent requestInfo:
                        // HITL: Workflow is waiting for external input
                        this._logger.LogInformation(
                            "[DIAG] RequestInfoEvent received: {RunId}, RequestId: {RequestId}, PortId: {PortId}",
                            request.RunId, requestInfo.Request.RequestId, requestInfo.Request.PortInfo.PortId);

                        var pendingRequest = MapToPendingExternalRequest(requestInfo.Request);

                        // Store checkpoint for resume - checkpoint is automatically created by framework at each superstep
                        var checkpointInfo = checkpointedRun.LastCheckpoint
                            ?? throw new InvalidOperationException("No checkpoint available for HITL resume. The workflow may not have completed a superstep.");
                        var checkpointData = JsonSerializer.SerializeToUtf8Bytes(
                            new CheckpointInfoDto(request.RunId, checkpointInfo.CheckpointId));

                        await stateClient.SaveCheckpointAsync(
                            request.RunId,
                            new WorkflowCheckpointData
                            {
                                CheckpointId = checkpointInfo.CheckpointId,
                                Data = checkpointData,
                                CreatedAt = DateTimeOffset.UtcNow
                            },
                            etag: null,
                            cancellationToken);

                        // Store pending request via callback
                        await stateClient.RecordPendingRequestAsync(
                            request.RunId,
                            pendingRequest,
                            etag: null,
                            cancellationToken);

                        // Update status to WaitingForSignal
                        await stateClient.UpdateStatusAsync(
                            request.RunId,
                            new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.WaitingForSignal },
                            etag: null,
                            cancellationToken);

                        // Emit signal requested event (will pause SSE stream here)
                        await writer.WriteAsync(new WorkflowSignalRequestedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = DateTimeOffset.UtcNow,
                            Request = pendingRequest
                        }, cancellationToken);

                        // Return here - workflow will be resumed via ResumeAsync
                        return;

                    case WorkflowOutputEvent outputEvent:
                        this._logger.LogInformation("Workflow produced output: {RunId}", request.RunId);
                        // Output is handled by completion below
                        break;

                    case WorkflowErrorEvent errorEvent:
                        var frameworkError = errorEvent.Data?.ToString() ?? "Unknown error";
                        throw new InvalidOperationException(frameworkError);

                    case WorkflowWarningEvent warningEvent:
                        this._logger.LogWarning(
                            "Workflow warning: {RunId} - {Warning}",
                            request.RunId, warningEvent.Data);
                        break;

                    case SuperStepCompletedEvent superStepCompleted:
                        this._logger.LogDebug(
                            "[DIAG] SuperStepCompletedEvent: RunId={RunId}, StepNumber={StepNumber}, HasPendingMessages={HasPendingMessages}, HasPendingRequests={HasPendingRequests}",
                            request.RunId,
                            superStepCompleted.StepNumber,
                            superStepCompleted.CompletionInfo?.HasPendingMessages,
                            superStepCompleted.CompletionInfo?.HasPendingRequests);
                        break;

                    default:
                        this._logger.LogDebug("[DIAG] Unhandled event type: {EventType}", evt.GetType().Name);
                        break;
                }
            }

            this._logger.LogDebug("[DIAG] WatchStreamAsync loop exited normally for workflow {RunId}", request.RunId);

            // Workflow completed successfully
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed },
                etag: null,
                cancellationToken);

            await writer.WriteAsync(new WorkflowCompletedSignalEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber++,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation("Workflow cancelled: {RunId}", request.RunId);

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Cancelled);

            await writer.WriteAsync(new WorkflowCancelledEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Workflow failed: {RunId}", request.RunId);

            var errorInfo = new WorkflowErrorInfo
            {
                Code = "WORKFLOW_EXECUTION_ERROR",
                Message = ex.Message,
                StackTrace = ex.StackTrace
            };

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Failed, errorInfo);

            await writer.WriteAsync(new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Error = errorInfo
            }, CancellationToken.None);
        }
        finally
        {
            writer.Complete();

            if (checkpointedRun is not null)
            {
                await checkpointedRun.DisposeAsync();
            }
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<WorkflowStatusEvent> ResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.ResumeInternalAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<WorkflowStatusEvent> ResumeInternalAsync(
        WorkflowResumeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartWorkflowResume(request.RunId, request.WorkflowName, request.Signal.RequestId);

        this._logger.LogInformation("Resuming workflow: {RunId} with signal for request {RequestId}",
            request.RunId, request.Signal.RequestId);

        // Create state client
        var stateClient = CreateStateClient(request.CallbackBaseUrl);

        // Resolve a framework Workflow instance
        var workflow = this._serviceProvider.GetKeyedService<Workflow>(request.WorkflowName);
        if (workflow is null)
        {
            this._logger.LogError("Unknown workflow: {WorkflowName}", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"Unknown workflow: '{request.WorkflowName}'"));
            yield return new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow,
                Error = new WorkflowErrorInfo
                {
                    Code = "WORKFLOW_NOT_FOUND",
                    Message = $"Unknown workflow: '{request.WorkflowName}'"
                }
            };
            yield break;
        }

        this._logger.LogDebug("Resuming workflow: {WorkflowName}", request.WorkflowName);

        // Use a channel to safely collect events
        var channel = Channel.CreateUnbounded<WorkflowStatusEvent>();

        // Execute framework resume in background and write to channel
        var resumeTask = this.ResumeWorkflowCoreAsync(
            request,
            workflow,
            stateClient,
            channel.Writer,
            cancellationToken);

        // Read events from channel and yield them
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Ensure resume task completes
        await resumeTask;
    }

    /// <summary>
    /// Resumes a <see cref="Workflow"/> from a checkpoint with an external response.
    /// </summary>
    private async Task ResumeWorkflowCoreAsync(
        WorkflowResumeRequest request,
        Workflow workflow,
        GatewayWorkflowStateClient stateClient,
        ChannelWriter<WorkflowStatusEvent> writer,
        CancellationToken cancellationToken)
    {
        Checkpointed<StreamingRun>? checkpointedRun = null;
        int sequenceNumber = 0;
        Dictionary<string, DateTimeOffset> stepStartTimes = [];
        Dictionary<string, string> executorStepIds = []; // Map executorId to stepId for completion

        try
        {
            // Require checkpoint data for workflow resume
            if (request.CheckpointData is null || request.CheckpointData.Length == 0)
            {
                throw new InvalidOperationException(
                    "Workflow resume requires checkpoint data. The workflow cannot be resumed without a valid checkpoint.");
            }

            // Deserialize checkpoint info from the stored data
            // The checkpoint data contains JSON-serialized CheckpointInfo (runId + checkpointId)
            var checkpointManager = GatewayCheckpointStore.CreateCheckpointManager(stateClient, request.RunId);
            var checkpointJson = System.Text.Encoding.UTF8.GetString(request.CheckpointData);
            var checkpointInfo = JsonSerializer.Deserialize<CheckpointInfoDto>(checkpointJson)
                ?? throw new InvalidOperationException("Failed to deserialize checkpoint info from checkpoint data.");
            var frameworkCheckpointInfo = new CheckpointInfo(checkpointInfo.RunId, checkpointInfo.CheckpointId);

            // Update status to Running
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running },
                etag: null,
                cancellationToken);

            // Clear the pending request
            await stateClient.ClearPendingRequestAsync(
                request.RunId,
                request.Signal.RequestId,
                etag: null,
                cancellationToken);

            // Emit signal received event
            await writer.WriteAsync(new WorkflowSignalReceivedEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber++,
                Timestamp = DateTimeOffset.UtcNow,
                RequestId = request.Signal.RequestId,
                Response = request.Signal.Response
            }, cancellationToken);

            // Resume from checkpoint
            var executionEnvironment = InProcessExecution.WithLogger(this._logger);
            checkpointedRun = await executionEnvironment.ResumeStreamAsync(
                workflow,
                frameworkCheckpointInfo,
                checkpointManager,
                request.RunId,
                cancellationToken);

            // Deserialize the response data for later use
            var responseData = this.DeserializeInput(request.Signal.Response);
            var expectedRequestId = request.Signal.RequestId;
            var responseSent = false;

            // Process framework events and map to contract events
            await foreach (var evt in checkpointedRun.Run.WatchStreamAsync(cancellationToken))
            {
                switch (evt)
                {
                    case RequestInfoEvent requestInfo when !responseSent && requestInfo.Request.RequestId == expectedRequestId:
                        // This is the pending request being replayed - send our response
                        this._logger.LogDebug(
                            "Sending response for request {RequestId} in workflow {RunId}",
                            requestInfo.Request.RequestId, request.RunId);

                        var response = requestInfo.Request.CreateResponse(responseData);
                        await checkpointedRun.Run.SendResponseAsync(response);
                        responseSent = true;
                        break;

                    case RequestInfoEvent requestInfo:
                        // HITL again: Workflow is waiting for another external input
                        this._logger.LogInformation(
                            "Workflow paused for external input again: {RunId}, RequestId: {RequestId}",
                            request.RunId, requestInfo.Request.RequestId);

                        var pendingRequest = MapToPendingExternalRequest(requestInfo.Request);

                        // Store checkpoint for resume - checkpoint is automatically created by framework at each superstep
                        var newCheckpointInfo = checkpointedRun.LastCheckpoint
                            ?? throw new InvalidOperationException("No checkpoint available for HITL resume. The workflow may not have completed a superstep.");
                        var newCheckpointData = JsonSerializer.SerializeToUtf8Bytes(
                            new CheckpointInfoDto(request.RunId, newCheckpointInfo.CheckpointId));

                        await stateClient.SaveCheckpointAsync(
                            request.RunId,
                            new WorkflowCheckpointData
                            {
                                CheckpointId = newCheckpointInfo.CheckpointId,
                                Data = newCheckpointData,
                                CreatedAt = DateTimeOffset.UtcNow
                            },
                            etag: null,
                            cancellationToken);

                        // Store pending request via callback
                        await stateClient.RecordPendingRequestAsync(
                            request.RunId,
                            pendingRequest,
                            etag: null,
                            cancellationToken);

                        // Update status to WaitingForSignal
                        await stateClient.UpdateStatusAsync(
                            request.RunId,
                            new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.WaitingForSignal },
                            etag: null,
                            cancellationToken);

                        // Emit signal requested event
                        await writer.WriteAsync(new WorkflowSignalRequestedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = DateTimeOffset.UtcNow,
                            Request = pendingRequest
                        }, cancellationToken);

                        return; // Pause here for next resume

                    case ExecutorInvokedEvent executorInvoked:
                        var stepId = $"step_{sequenceNumber}";
                        var startedAt = DateTimeOffset.UtcNow;
                        stepStartTimes[executorInvoked.ExecutorId] = startedAt;
                        executorStepIds[executorInvoked.ExecutorId] = stepId;

                        var stepStartedRecord = new WorkflowStepStartedRecord
                        {
                            StepId = stepId,
                            ExecutorId = executorInvoked.ExecutorId,
                            ExecutorName = executorInvoked.ExecutorId,
                            StartedAt = startedAt
                        };

                        // Record step to Gateway for persistence
                        await stateClient.RecordStepStartedAsync(
                            request.RunId,
                            stepStartedRecord,
                            etag: null,
                            cancellationToken);

                        // Emit SSE event
                        await writer.WriteAsync(new WorkflowStepStartedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = startedAt,
                            Step = stepStartedRecord
                        }, cancellationToken);
                        break;

                    case ExecutorCompletedEvent executorCompleted:
                        var completedStepId = executorStepIds.GetValueOrDefault(executorCompleted.ExecutorId, $"step_{sequenceNumber}");
                        var stepStartedAt = stepStartTimes.GetValueOrDefault(executorCompleted.ExecutorId, DateTimeOffset.UtcNow);
                        var completedAt = DateTimeOffset.UtcNow;
                        var durationMs = (long)(completedAt - stepStartedAt).TotalMilliseconds;

                        var stepCompletedRecord = new WorkflowStepCompletedRecord
                        {
                            StepId = completedStepId,
                            ExecutorId = executorCompleted.ExecutorId,
                            CompletedAt = completedAt,
                            Output = executorCompleted.Data is not null
                                ? WorkflowMessage.Create(executorCompleted.Data)
                                : null,
                            DurationMs = durationMs
                        };

                        // Record step completion to Gateway for persistence
                        await stateClient.RecordStepCompletedAsync(
                            request.RunId,
                            stepCompletedRecord,
                            etag: null,
                            cancellationToken);

                        // Emit SSE event
                        await writer.WriteAsync(new WorkflowStepCompletedEvent
                        {
                            RunId = request.RunId,
                            SequenceNumber = sequenceNumber++,
                            Timestamp = completedAt,
                            Step = stepCompletedRecord
                        }, cancellationToken);
                        break;

                    case WorkflowOutputEvent outputEvent:
                        this._logger.LogInformation("Resumed workflow produced output: {RunId}", request.RunId);
                        break;

                    case WorkflowErrorEvent errorEvent:
                        var frameworkError = errorEvent.Data?.ToString() ?? "Unknown error";
                        throw new InvalidOperationException(frameworkError);

                    case WorkflowWarningEvent warningEvent:
                        this._logger.LogWarning(
                            "Resumed workflow warning: {RunId} - {Warning}",
                            request.RunId, warningEvent.Data);
                        break;
                }
            }

            // Workflow completed successfully
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed },
                etag: null,
                cancellationToken);

            await writer.WriteAsync(new WorkflowCompletedSignalEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber++,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation("Workflow resume cancelled: {RunId}", request.RunId);

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Cancelled);

            await writer.WriteAsync(new WorkflowCancelledEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Workflow resume failed: {RunId}", request.RunId);

            var errorInfo = new WorkflowErrorInfo
            {
                Code = "WORKFLOW_RESUME_ERROR",
                Message = ex.Message,
                StackTrace = ex.StackTrace
            };

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Failed, errorInfo);

            await writer.WriteAsync(new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Error = errorInfo
            }, CancellationToken.None);
        }
        finally
        {
            writer.Complete();

            if (checkpointedRun is not null)
            {
                await checkpointedRun.DisposeAsync();
            }
        }
    }

    private async Task SafeUpdateStatusAsync(
        GatewayWorkflowStateClient stateClient,
        string runId,
        WorkflowRunStatus status,
        WorkflowErrorInfo? error = null)
    {
        try
        {
            await stateClient.UpdateStatusAsync(
                runId,
                new WorkflowRunStatusUpdate
                {
                    Status = status,
                    Error = error
                },
                etag: null,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to update workflow status to {Status}: {RunId}", status, runId);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkflowDefinitionInfo>> GetAvailableWorkflowsAsync(
        CancellationToken cancellationToken = default)
    {
        // Discover all Workflow instances registered as keyed services
        var workflows = this._serviceProvider.GetKeyedServices<Workflow>(KeyedService.AnyKey)
            .Where(w => w is not null)
            .Select(w => new WorkflowDefinitionInfo
            {
                Name = w!.Name ?? "unnamed",
                DisplayName = w.Name ?? "unnamed",
                Description = w.Description ?? $"Workflow: {w.GetType().FullName}"
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkflowDefinitionInfo>>(workflows);
    }

    private static GatewayWorkflowStateClient CreateStateClient(string callbackBaseUrl)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(callbackBaseUrl)
        };
        return new GatewayWorkflowStateClient(httpClient);
    }

    /// <summary>
    /// Deserializes the workflow input message to an object.
    /// Attempts to resolve the actual type from TypeName for proper routing in the workflow framework.
    /// If the type cannot be resolved, tries to deserialize to one of the accepted input types.
    /// </summary>
    /// <param name="input">The workflow message containing the serialized input data.</param>
    /// <param name="acceptedInputTypes">Optional array of types that the workflow accepts as input.
    /// When type resolution from TypeName fails, deserialization is attempted for each of these types.</param>
    private object DeserializeInput(WorkflowMessage input, Type[]? acceptedInputTypes = null)
    {
        // Try to resolve the actual type from TypeName for proper workflow routing
        if (!string.IsNullOrEmpty(input.TypeName))
        {
            // Try to find the type in the current assemblies
            var type = Type.GetType(input.TypeName);
            if (type is null)
            {
                // Try to find in all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(input.TypeName.Split(',')[0].Trim());
                    if (type is not null)
                    {
                        break;
                    }
                }
            }

            if (type is not null)
            {
                try
                {
                    var typedResult = input.Data.Deserialize(type);
                    if (typedResult is not null)
                    {
                        this._logger.LogDebug("[DIAG] Successfully deserialized input to type {TypeName}", type.FullName);
                        return typedResult;
                    }
                }
                catch (JsonException ex)
                {
                    this._logger.LogDebug(ex, "[DIAG] Failed to deserialize input to type {TypeName}, trying accepted types", type.FullName);
                }
            }
            else
            {
                this._logger.LogDebug("[DIAG] Could not resolve type from TypeName: {TypeName}, trying accepted types", input.TypeName);
            }
        }

        // Try to deserialize to one of the accepted input types
        if (acceptedInputTypes is { Length: > 0 })
        {
            foreach (var acceptedType in acceptedInputTypes)
            {
                try
                {
                    var typedResult = input.Data.Deserialize(acceptedType);
                    if (typedResult is not null)
                    {
                        this._logger.LogDebug("[DIAG] Successfully deserialized input to accepted type {TypeName}", acceptedType.FullName);
                        return typedResult;
                    }
                }
                catch (JsonException ex)
                {
                    this._logger.LogDebug(ex, "[DIAG] Failed to deserialize input to accepted type {TypeName}", acceptedType.FullName);
                }
            }
        }

        // Fallback: Try to deserialize as a dictionary for generic input handling
        try
        {
            var dict = input.Data.Deserialize<Dictionary<string, object?>>();
            if (dict is not null)
            {
                this._logger.LogDebug("[DIAG] Deserialized input as Dictionary<string, object?>");
                return dict;
            }
        }
        catch (JsonException)
        {
            // Fall through to raw data
        }

        // Return the raw JsonElement as a string representation
        this._logger.LogDebug("[DIAG] Returning raw input data as string");
        return input.Data.GetRawText();
    }

    /// <summary>
    /// Maps a framework <see cref="ExternalRequest"/> to a contract <see cref="PendingExternalRequest"/>.
    /// </summary>
    private static PendingExternalRequest MapToPendingExternalRequest(ExternalRequest request)
    {
        return new PendingExternalRequest
        {
            RequestId = request.RequestId,
            PortId = request.PortInfo.PortId,
            RequestTypeName = request.PortInfo.RequestType.TypeName,
            ResponseTypeName = request.PortInfo.ResponseType.TypeName,
            RequestData = WorkflowMessage.Create(request.Data.As<object?>() ?? new { }),
            Title = $"Input required: {request.PortInfo.PortId}",
            Description = $"Workflow is waiting for input at port '{request.PortInfo.PortId}'",
            RequestedAt = DateTimeOffset.UtcNow,
            UIHints = new Dictionary<string, string>
            {
                ["portId"] = request.PortInfo.PortId,
                ["requestType"] = request.PortInfo.RequestType.TypeName,
                ["responseType"] = request.PortInfo.ResponseType.TypeName
            }
        };
    }
}

/// <summary>
/// DTO for serializing/deserializing checkpoint info to/from storage.
/// </summary>
internal sealed record CheckpointInfoDto(string RunId, string CheckpointId);
