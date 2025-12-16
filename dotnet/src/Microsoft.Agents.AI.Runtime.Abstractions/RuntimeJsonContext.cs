// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;

namespace Microsoft.Agents.AI.Runtime.Abstractions;

/// <summary>
/// Source-generated JSON serialization context for Runtime types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
// Workflow models
[JsonSerializable(typeof(WorkflowRun))]
[JsonSerializable(typeof(WorkflowRunSummary))]
[JsonSerializable(typeof(WorkflowMessage))]
[JsonSerializable(typeof(WorkflowSignal))]
[JsonSerializable(typeof(WorkflowStepInfo))]
[JsonSerializable(typeof(PendingExternalRequest))]
[JsonSerializable(typeof(WorkflowArtifactRecord))]
[JsonSerializable(typeof(WorkflowErrorInfo))]
[JsonSerializable(typeof(WorkflowCheckpointData))]
[JsonSerializable(typeof(WorkflowDefinitionInfo))]
[JsonSerializable(typeof(RequestPortDefinition))]
// Workflow requests
[JsonSerializable(typeof(StartWorkflowRequest))]
[JsonSerializable(typeof(ListWorkflowsRequest))]
[JsonSerializable(typeof(WorkflowExecutionRequest))]
[JsonSerializable(typeof(WorkflowResumeRequest))]
[JsonSerializable(typeof(AbortWorkflowRequest))]
[JsonSerializable(typeof(WorkflowRunStatusUpdate))]
[JsonSerializable(typeof(WorkflowStepStartedRecord))]
[JsonSerializable(typeof(WorkflowStepCompletedRecord))]
[JsonSerializable(typeof(ETagResponse))]
[JsonSerializable(typeof(WorkflowListResponse<WorkflowRunSummary>))]
[JsonSerializable(typeof(WorkflowListResponse<WorkflowRun>))]
// Workflow events (polymorphic)
[JsonSerializable(typeof(WorkflowStatusEvent))]
[JsonSerializable(typeof(WorkflowStartedEvent))]
[JsonSerializable(typeof(WorkflowStepStartedEvent))]
[JsonSerializable(typeof(WorkflowStepCompletedEvent))]
[JsonSerializable(typeof(WorkflowSignalRequestedEvent))]
[JsonSerializable(typeof(WorkflowSignalReceivedEvent))]
[JsonSerializable(typeof(WorkflowArtifactCreatedEvent))]
[JsonSerializable(typeof(WorkflowOutputDeltaEvent))]
[JsonSerializable(typeof(WorkflowOutputDelta))]
[JsonSerializable(typeof(WorkflowCompletedEvent))]
[JsonSerializable(typeof(WorkflowCompletedSignalEvent))]
[JsonSerializable(typeof(WorkflowFailedEvent))]
[JsonSerializable(typeof(WorkflowCancelledEvent))]
[JsonSerializable(typeof(WorkflowAbortedEvent))]
// Worker models
[JsonSerializable(typeof(WorkerRegistrationRequest))]
[JsonSerializable(typeof(WorkerRegistrationResponse))]
[JsonSerializable(typeof(WorkerProcessMetadata))]
[JsonSerializable(typeof(WorkflowExecutionResult))]
// Monitoring models
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(WorkerStatus))]
[JsonSerializable(typeof(WorkflowMonitoringSummary))]
[JsonSerializable(typeof(WorkflowMetricsSnapshot))]
[JsonSerializable(typeof(MonitoringEvent))]
[JsonSerializable(typeof(WorkflowMonitoringEvent))]
[JsonSerializable(typeof(WorkerMonitoringEvent))]
[JsonSerializable(typeof(SystemMonitoringEvent))]
[JsonSerializable(typeof(WorkflowEventPayload))]
[JsonSerializable(typeof(WorkerEventPayload))]
// Collections
[JsonSerializable(typeof(WorkflowDefinitionInfo[]))]
[JsonSerializable(typeof(WorkerStatus[]))]
[JsonSerializable(typeof(WorkflowMonitoringSummary[]))]
public partial class RuntimeJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Default options for JSON serialization matching the source generator configuration.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
}
