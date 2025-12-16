// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Runtime.Abstractions.Workflows;

// ============ Request Types ============

/// <summary>
/// Request to start a new workflow.
/// </summary>
public sealed class StartWorkflowRequest
{
    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("input")]
    public required WorkflowMessage Input { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; init; }
}

/// <summary>
/// Request to list workflows.
/// </summary>
public sealed class ListWorkflowsRequest
{
    [JsonPropertyName("status")]
    public WorkflowRunStatus? Status { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;

    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}

/// <summary>
/// Request to execute a workflow (Gateway -> Worker).
/// </summary>
public sealed class WorkflowExecutionRequest
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("input")]
    public required WorkflowMessage Input { get; init; }

    [JsonPropertyName("callbackBaseUrl")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String required for JSON serialization")]
    public required string CallbackBaseUrl { get; init; }

    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; init; }
}

/// <summary>
/// Request to resume a workflow (Gateway -> Worker).
/// </summary>
public sealed class WorkflowResumeRequest
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("callbackBaseUrl")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String required for JSON serialization")]
    public required string CallbackBaseUrl { get; init; }

    [JsonPropertyName("signal")]
    public required WorkflowSignal Signal { get; init; }

    /// <summary>
    /// The ID of the checkpoint to resume from.
    /// </summary>
    [JsonPropertyName("checkpointId")]
    public string? CheckpointId { get; init; }

    [JsonPropertyName("checkpointData")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required for binary JSON serialization")]
    [Obsolete("Use CheckpointId instead. CheckpointData is no longer used for passing checkpoint information.")]
    public byte[]? CheckpointData { get; init; }
}

/// <summary>
/// Request to abort a workflow.
/// </summary>
public sealed class AbortWorkflowRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

// ============ State Update Types (Worker -> Gateway) ============

/// <summary>
/// Status update for a workflow run.
/// </summary>
public sealed class WorkflowRunStatusUpdate
{
    [JsonPropertyName("status")]
    public required WorkflowRunStatus Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("error")]
    public WorkflowErrorInfo? Error { get; init; }
}

/// <summary>
/// Record that a step started.
/// </summary>
public sealed class WorkflowStepStartedRecord
{
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    [JsonPropertyName("executorId")]
    public required string ExecutorId { get; init; }

    [JsonPropertyName("executorName")]
    public string? ExecutorName { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Record that a step completed.
/// </summary>
public sealed class WorkflowStepCompletedRecord
{
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    [JsonPropertyName("executorId")]
    public required string ExecutorId { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset CompletedAt { get; init; }

    [JsonPropertyName("output")]
    public WorkflowMessage? Output { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }
}

// ============ State Update Response Types ============

/// <summary>
/// Response containing the new ETag after a state update.
/// Used for optimistic concurrency control in workflow state updates.
/// </summary>
public sealed class ETagResponse
{
    /// <summary>
    /// The new ETag value after the state update.
    /// </summary>
    [JsonPropertyName("eTag")]
    public required string ETag { get; init; }
}

// ============ Generic List Response ============

/// <summary>
/// Generic list response for paginated workflow results.
/// </summary>
public sealed class WorkflowListResponse<T>
{
    /// <summary>
    /// The object type, always "list".
    /// </summary>
    [JsonPropertyName("object")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches OpenAI API convention")]
    public string Object => "list";

    /// <summary>
    /// The list of items.
    /// </summary>
    [JsonPropertyName("data")]
    public required List<T> Data { get; init; }

    /// <summary>
    /// The ID of the first item in the list.
    /// </summary>
    [JsonPropertyName("firstId")]
    public string? FirstId { get; init; }

    /// <summary>
    /// The ID of the last item in the list.
    /// </summary>
    [JsonPropertyName("lastId")]
    public string? LastId { get; init; }

    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public required bool HasMore { get; init; }
}
